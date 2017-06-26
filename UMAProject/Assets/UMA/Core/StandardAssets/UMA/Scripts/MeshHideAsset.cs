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

        public BitArray vertexFlags { get { return _vertexFlags; }}
        private BitArray _vertexFlags; //Flag of all vertices of whether this asset wants that vertex hidden or not.

        [SerializeField]
        private bool[] _serializedFlags;

        public int VertexCount 
        { 
            get 
            {
                if (_vertexFlags != null)
                    return _vertexFlags.Length;
                else
                    return 0;
            }
        }   

        public void OnBeforeSerialize()
        {
            if (_vertexFlags == null)
                return;
            
            if (VertexCount > 0)
            {
                if (_serializedFlags == null)
                    _serializedFlags = new bool[_vertexFlags.Length];
                else
                    _serializedFlags.Initialize();

                _vertexFlags.CopyTo(_serializedFlags, 0);
            }
        }

        public void OnAfterDeserialize()
        {
            if (_serializedFlags == null)
            {
                Debug.LogError("SerializedFlags is null!");
                return;
            }

            if (_serializedFlags.Length > 0)
                _vertexFlags = new BitArray(_serializedFlags);
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

            _vertexFlags = new BitArray(asset.meshData.vertexCount);
            _serializedFlags = new bool[asset.meshData.vertexCount];
            Debug.Log("MeshHideAsset Initialized!");
        }

        public int NumHiddenVertices()
        {
            int hiddenCount = 0;
            for (int i = 0; i < VertexCount; i++)
            {
                if (vertexFlags[i])
                    hiddenCount++;
            }
            return hiddenCount;
        }
/*
        public Vector3[] GetUnhiddenVertices()
        {
            Vector3[] vertices = new Vector3[NumUnhiddenVertices()];
            int index = 0;

            for (int i = 0; i < VertexCount; i++)
            {
                if (!vertexFlags[i])
                {
                    vertices[index] = asset.meshData.vertices[i];
                    index++;
                }
            }

            return vertices;
        }*/

        /// <summary>
        ///  Set a vertex by position and if found set it's boolean value
        /// </summary>
        [ExecuteInEditMode]
        public void SetVertexFlag(Vector3 pos, bool flag)
        {
            if (_vertexFlags == null)
            {
                Debug.LogError("Vertex Array not initialized!");
                return;
            }
            Debug.Log("SetVertexFlag");
            for (int i = 0; i < asset.meshData.vertexCount; i++)
            {
                if (asset.meshData.vertices[i] == pos)
                {
                    Debug.Log("Found vertex to set");
                    _vertexFlags[i] = flag;
                    break;
                }
            }
            OnBeforeSerialize();
        }

        public static BitArray GenerateMask( List<MeshHideAsset> assets )
        {
            List<BitArray> flags = new List<BitArray>();
            foreach (MeshHideAsset asset in assets)
                flags.Add(asset.vertexFlags);

            return CombineVertexFlags(flags);
        }

        public static BitArray CombineVertexFlags( List<BitArray> flags)
        {
            if (flags == null || flags.Count <= 0)
                return null;
            
            BitArray final = new BitArray(flags[0]);

            for (int i = 1; i < flags.Count; i++)
            {
                if( flags[i].Count == flags[0].Count)
                    final.Or(flags[i]);
            }

            return final;
        }

        [ExecuteInEditMode]
        public static UMAMeshData FilterMeshData( UMAMeshData meshData, BitArray vertexFlags )
        {
            if (meshData == null || vertexFlags == null)
                return null;

            if (vertexFlags.Count != meshData.vertexCount)
                return null;

            UMAMeshData newData = new UMAMeshData();
            newData.submeshes = new SubMeshTriangles[meshData.subMeshCount];
            newData.subMeshCount = meshData.subMeshCount;

            int[] vertexIndicesMap = new int[vertexFlags.Count];
            UMABoneWeight[] newBoneWeights = new UMABoneWeight[vertexFlags.Count];
            Vector3[] newVertices = new Vector3[vertexFlags.Count];
            Vector3[] newNormals = new Vector3[vertexFlags.Count];
            Vector4[] newTangents = new Vector4[vertexFlags.Count];
            Vector2[] newUV = new Vector2[vertexFlags.Count];
            Vector2[] newUV2 = new Vector2[vertexFlags.Count];
            Vector2[] newUV3 = new Vector2[vertexFlags.Count];
            Vector2[] newUV4 = new Vector2[vertexFlags.Count];
            UnityEngine.Color32[] newColors32 = new UnityEngine.Color32[vertexFlags.Count];
            int index = 0;

            bool has_normals = (meshData.normals != null && meshData.normals.Length != 0);
            bool has_tangents = (meshData.tangents != null && meshData.tangents.Length != 0);
            bool has_uv = (meshData.uv != null && meshData.uv.Length != 0 );
            bool has_uv2 = (meshData.uv2 != null && meshData.uv2.Length != 0);
            bool has_uv3 = (meshData.uv3 != null && meshData.uv3.Length != 0);
            bool has_uv4 = (meshData.uv4 != null && meshData.uv4.Length != 0);
            bool has_colors32 = (meshData.colors32 != null && meshData.colors32.Length != 0);

            //First let's filter out our vertices and store a map of the new vertex indexes.
            for (int i = 0; i < vertexFlags.Count; i++)
            {
                if (!vertexFlags[i])
                {
                    newVertices[index] = meshData.vertices[i];
                    newBoneWeights[index] = meshData.boneWeights[i];
                    if (has_normals) newNormals[index] = meshData.normals[i];
                    if (has_tangents) newTangents[index] = meshData.tangents[i];
                    if (has_uv) newUV[index] = meshData.uv[i];
                    if (has_uv2) newUV2[index] = meshData.uv2[i];
                    if (has_uv3) newUV3[index] = meshData.uv3[i];
                    if (has_uv4) newUV4[index] = meshData.uv4[i];
                    if (has_colors32) newColors32[index] = meshData.colors32[i];

                    vertexIndicesMap[i] = index;
                    index++;
                }
                else
                    vertexIndicesMap[i] = -1;
            }
            if (index > 0)
            {
                newData.vertices = newVertices;
                newData.vertexCount = newVertices.Length;
                newData.boneWeights = newBoneWeights;
                newData.normals = newNormals;
                newData.tangents = newTangents;
                newData.uv = newUV;
                newData.uv2 = newUV2;
                newData.uv3 = newUV3;
                newData.uv4 = newUV4;
                newData.colors32 = newColors32;

                newData.bindPoses = meshData.bindPoses;
                newData.boneNameHashes = meshData.boneNameHashes;
                newData.unityBoneWeights = meshData.unityBoneWeights;
                newData.rootBoneHash = meshData.rootBoneHash;
                newData.umaBoneCount = meshData.umaBoneCount;
                newData.umaBones = meshData.umaBones;

                newData.subMeshCount = meshData.subMeshCount;
            }

            //Now, let's rebuild our triangle lists and point their indexes to the new correct one.
            for (int i = 0; i < meshData.subMeshCount; i++)
            {
                List<int> newIndices = new List<int>();
                for (int j = 0; j < meshData.submeshes[i].triangles.Length; j+=3)
                {
                    int index0 = meshData.submeshes[i].triangles[j];
                    int index1 = meshData.submeshes[i].triangles[j+1];
                    int index2 = meshData.submeshes[i].triangles[j+2];

                    if (vertexIndicesMap[index0] < 0 || vertexIndicesMap[index1] < 0 || vertexIndicesMap[index2] < 0)
                        continue;
                    else
                    {
                        newIndices.Add(vertexIndicesMap[index0]);
                        newIndices.Add(vertexIndicesMap[index1]);
                        newIndices.Add(vertexIndicesMap[index2]);
                    }                        
                }
                if (newIndices.Count > 0)
                {
                    newData.submeshes[i] = new SubMeshTriangles();
                    newData.submeshes[i].triangles = newIndices.ToArray();
                }
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

    public class MeshHideEditObject : MonoBehaviour
    {
        public MeshHideAsset HideAsset;

        [Header("Cube Gizmo Control")]
        public Color CubeColorActive = Color.black;
        public Color CubeColorHidden = Color.red;
        [Range( 0.001f, 0.05f )]
        public float CubeSize = 0.01f;
       
        private Vector3 _CubeSize = new Vector3( 0.01f, 0.01f, 0.01f );
        public BoxCollider pickCollider;


        void OnDrawGizmosSelected()
        {
            if (HideAsset == null)
                return;
            
            _CubeSize.x = CubeSize; _CubeSize.y = CubeSize; _CubeSize.z = CubeSize;

            for( int i = 0; i < HideAsset.asset.meshData.vertexCount; i++ )
            {
                if (HideAsset.vertexFlags[i]==true)
                    Gizmos.color = CubeColorHidden;
                else
                    Gizmos.color = CubeColorActive;

                Gizmos.DrawCube(HideAsset.asset.meshData.vertices[i], _CubeSize);
            }
        }
    }
}