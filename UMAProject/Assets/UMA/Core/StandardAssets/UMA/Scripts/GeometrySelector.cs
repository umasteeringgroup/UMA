using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class GeometrySelector : MonoBehaviour 
    {
        public bool showWireframe = true;
        public MeshHideAsset meshAsset;

        //public MeshHideAsset HideAsset;
        public Mesh sharedMesh
        {
            get { return _sharedMesh; }
            set { _sharedMesh = (Mesh)Instantiate(value); Initialize(); }
        }
        private Mesh _sharedMesh;

        private MeshRenderer _meshRenderer;
        //Use 0 for unselected and 1 for selected
        private Material[] _Materials;

        //public Dictionary<int, int> selectedTriangles = new Dictionary<int, int>();
        public List<int> selectedTriangles = new List<int>();

        public void Initialize()
        {
            gameObject.name = "GeometrySelector";
            if (_sharedMesh == null)
            {
                Debug.LogWarning("GeometrySelector: Initializing with no mesh!");
                return;
            }
                
            if( !gameObject.GetComponent<MeshFilter>())
            {
                MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
                meshFilter.mesh = _sharedMesh;
            }

            if( !gameObject.GetComponent<MeshRenderer>())
            {                
                _meshRenderer = gameObject.AddComponent<MeshRenderer>();
                _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _meshRenderer.receiveShadows = false;
            }

            if( !gameObject.GetComponent<MeshCollider>())
            {
                MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = false;
                meshCollider.sharedMesh = _sharedMesh;;
            }

            if (_Materials == null)
            {
                _Materials = new Material[2];

                //Selected
                _Materials[1] = new Material(Shader.Find("Standard"));
                _Materials[1].name = "Selected";
                _Materials[1].color = Color.red;

                //UnSelected
                _Materials[0] = new Material(Shader.Find("Standard"));
                _Materials[0].name = "UnSelected";
                _Materials[0].color = Color.gray;

                _sharedMesh.subMeshCount = 2;
                _meshRenderer.materials = _Materials;
            }
        }

        public void InitializeFromMeshData(UMAMeshData meshData)
        {
            if (meshData == null)
            {
                Debug.LogError("InitializeFromMeshData: meshData is null!");
                return;
            }

            _sharedMesh = new Mesh();
            _sharedMesh.subMeshCount = meshData.subMeshCount;
            _sharedMesh.vertices = meshData.vertices;
            _sharedMesh.normals = meshData.normals;
            _sharedMesh.tangents = meshData.tangents;
            _sharedMesh.uv = meshData.uv;
            _sharedMesh.uv2 = meshData.uv2;
            _sharedMesh.uv3 = meshData.uv3;
            _sharedMesh.uv4 = meshData.uv4;
            _sharedMesh.colors32 = meshData.colors32;

            _sharedMesh.triangles = new int[0];
            for (int i = 0; i < meshData.subMeshCount; i++)
                _sharedMesh.SetTriangles(meshData.submeshes[i].triangles, i);

            Initialize();
        }

        public void UpdateSelectionMesh()
        {
            int selectedCount = selectedTriangles.Count * 3;
            int[] newSelectedTriangles = new int[selectedCount];
            int selectedIndex = 0;

            for (int i = 0; i < sharedMesh.triangles.Length; i+=3)
            {
                if (selectedTriangles.Contains(i))
                {
                    newSelectedTriangles[selectedIndex] = sharedMesh.triangles[i];
                    newSelectedTriangles[selectedIndex + 1] = sharedMesh.triangles[i + 1];
                    newSelectedTriangles[selectedIndex + 2] = sharedMesh.triangles[i + 2];
                    selectedIndex += 3;
                }
            }

            sharedMesh.SetTriangles(newSelectedTriangles, 1);
        }

        void OnDrawGizmos()
        {           
            if (_sharedMesh == null)
                return;

            if (showWireframe)
                Gizmos.DrawWireMesh(_sharedMesh);
        }
    }
}
