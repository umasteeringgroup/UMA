using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace UMA
{
    public class MeshHideAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField]
        public SlotDataAsset asset //The asset we want to apply mesh hiding to if found in the generated UMA;
        {
            get{ return _asset; }
            set{ _asset = value; Initialize(); }
        }
        [SerializeField, HideInInspector]
        private SlotDataAsset _asset;

        public BitArray[] triangleFlags { get { return _triangleFlags; }}
        private BitArray[] _triangleFlags; //Flag of all triangles of whether this asset wants that triangle hidden or not.


        [System.Serializable]
        public class serializedFlags
        {
            public bool[] flags;

            public serializedFlags(int count)
            {
                flags = new bool[count];
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
                    return 0;
            }
        }

        public int TriangleCount 
        { 
            get 
            {
                if (_triangleFlags != null)
                {
                    int total = 0;
                    for (int i = 0; i < _triangleFlags.Length; i++)
                        total += _triangleFlags[i].Count;

                    return total;
                }
                else
                    return 0;
            }
        }   

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
                    return 0;
            }
        }

        public void OnBeforeSerialize()
        {
            if (_triangleFlags == null)
                return;
            
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
                Debug.LogError("Serializing triangle flags failed!");
        }

        public void OnAfterDeserialize()
        {
            if (_serializedFlags == null)
            {
                Debug.LogError("SerializedFlags is null!");
                return;
            }

            if (_serializedFlags.Length > 0)
            {
                _triangleFlags = new BitArray[_serializedFlags.Length];
                for (int i = 0; i < _serializedFlags.Length; i++)
                {
                    _triangleFlags[i] = new BitArray(_serializedFlags[i].flags);
                }
            }
        }

        /// <summary>
        ///  Initialize this asset by creating a new boolean array
        /// </summary>
        [ExecuteInEditMode]
        public void Initialize()
        {
            if (asset == null)
            {
                Debug.LogError("MeshHideAsset: Asset is null!");
                return;
            }

            if (asset.meshData == null)
                return;

            _triangleFlags = new BitArray[asset.meshData.subMeshCount];
            for (int i = 0; i < asset.meshData.subMeshCount; i++)
            {
                _triangleFlags[i] = new BitArray(asset.meshData.submeshes[i].triangles.Length);
            }
        }

        /// <summary>
        ///  Set a vertex by position and if found set it's boolean value
        /// </summary>
        /// <param name="triangleIndex" The first index for the triangle to set>
        [ExecuteInEditMode]
        public void SetTriangleFlag(int triangleIndex, bool flag, int submesh = 0)
        {
            if (_triangleFlags == null)
            {
                Debug.LogError("Triangle Array not initialized!");
                return;
            }
                
            if (triangleIndex >= 0 && (_triangleFlags[submesh].Length - 3) > triangleIndex)
            {
                _triangleFlags[submesh][triangleIndex] = flag;
                _triangleFlags[submesh][triangleIndex+1] = flag;
                _triangleFlags[submesh][triangleIndex+2] = flag;
            }
        }

        [ExecuteInEditMode]
        public void SaveSelection( List<int> selection )
        {
            //Only works for submesh 0 for now
            _triangleFlags[0].SetAll(false);
            foreach (int index in selection)
            {
                SetTriangleFlag(index, true);
            }
        }

        public static BitArray[] GenerateMask( List<MeshHideAsset> assets )
        {
            List<BitArray[]> flags = new List<BitArray[]>();
            foreach (MeshHideAsset asset in assets)
                flags.Add(asset.triangleFlags);

            return CombineVertexFlags(flags);
        }

        public static BitArray[] CombineVertexFlags( List<BitArray[]> flags)
        {
            if (flags == null || flags.Count <= 0)
                return null;
            
            BitArray[] final = new BitArray[flags[0].Length];
            for(int i = 0; i < flags[0].Length; i++)
            {
                final[i] = new BitArray(flags[0][i]);
            }

            for (int i = 1; i < flags.Count; i++)
            {
                for (int j = 0; j < flags[i].Length; j++)
                {
                    if (flags[i][j].Count == flags[0][j].Count)
                        final[j].Or(flags[i][j]);
                }
            }

            return final;
        }

        [ExecuteInEditMode]
        public static UMAMeshData CreateMeshData( UMAMeshData meshData, BitArray[] triangleFlags )
        {
            if (meshData == null || triangleFlags == null)
            {
                Debug.LogWarning("FilterMeshData: meshData or triangleFlags are null!");
                return null;
            }

            if (triangleFlags.Length != meshData.subMeshCount)
            {
                Debug.LogWarning("FilterMeshData: triangleFlags count not equal to subMeshCount");
                return null;
            }

            UMAMeshData newData = new UMAMeshData();
            newData.submeshes = new SubMeshTriangles[meshData.subMeshCount];
            newData.subMeshCount = meshData.subMeshCount;

            bool has_normals = (meshData.normals != null && meshData.normals.Length != 0);

            newData.vertices = new Vector3[meshData.vertexCount];
            meshData.vertices.CopyTo(newData.vertices, 0);

            if(has_normals)
            {
                newData.normals = new Vector3[meshData.vertexCount];
                meshData.normals.CopyTo(newData.normals, 0);
            }
                
            for (int i = 0; i < meshData.subMeshCount; i++)
            {
                List<int> newTriangles = new List<int>();
                for (int j = 0; j < meshData.submeshes[i].triangles.Length; j++)
                {
                    if (!triangleFlags[i][j])
                        newTriangles.Add(meshData.submeshes[i].triangles[j]);
                }
                newData.submeshes[i] = new SubMeshTriangles();
                newData.submeshes[i].triangles = new int[newTriangles.Count];
                newTriangles.CopyTo(newData.submeshes[i].triangles);
            }

            return newData;
        }

        #if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/Mesh Hide Asset")]
        public static void CreateMeshHideAsset()
        {
            UMA.CustomAssetUtility.CreateAsset<MeshHideAsset>();
        }
        #endif
    }
}