using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
    [System.Serializable]
    public class MeshHideAsset : ScriptableObject 
    {
        [SerializeField]
        public SlotDataAsset asset //The asset we want to apply mesh hiding to if found in the generated UMA;
        {
            get{ return _asset; }
            set{ _asset = value; Initialize(); }
        }
        private SlotDataAsset _asset;

        public bool[] vertexFlags { get { return _vertexFlags; }}
        private bool[] _vertexFlags; //Flag of all vertices of whether this asset wants that vertex hidden or not.

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

            _vertexFlags = new bool[asset.meshData.vertexCount];
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