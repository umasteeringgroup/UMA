using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class GeometrySelector : MonoBehaviour 
    {
        public bool showWireframe = true;
        public MeshHideAsset meshAsset;

        public BitArray selectedTriangles;

        //public MeshHideAsset HideAsset;
        public Mesh sharedMesh
        {
            get { return _sharedMesh; }
            set { _sharedMesh = (Mesh)Instantiate(value); Initialize(); }
        }
        private Mesh _sharedMesh;

        public MeshRenderer meshRenderer
        {
            get { return _meshRenderer; }
        }
        private MeshRenderer _meshRenderer;

        public MeshCollider meshCollider
        {
            get { return _meshCollider; }
        }
        private MeshCollider _meshCollider;
        //Use 0 for unselected and 1 for selected
        private Material[] _Materials;

        public delegate void OnDoneEditing(BitArray selection);
        public OnDoneEditing doneEditing;

        public void Initialize()
        {
            gameObject.name = "GeometrySelector";
            if (_sharedMesh == null)
            {
                Debug.LogWarning("GeometrySelector: Initializing with no mesh!");
                return;
            }

            if (meshAsset != null)
            {
                if (meshAsset.asset.meshData.rootBoneHash == UMAUtils.StringToHash("Global"))
                    gameObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }

            gameObject.transform.hideFlags = HideFlags.NotEditable | HideFlags.HideInInspector;

            if (selectedTriangles == null)
                selectedTriangles = new BitArray(_sharedMesh.triangles.Length);
                
            if( !gameObject.GetComponent<MeshFilter>())
            {
                MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
                meshFilter.mesh = _sharedMesh;
                meshFilter.hideFlags = HideFlags.HideInInspector;
            }

            if( !gameObject.GetComponent<MeshRenderer>())
            {                
                _meshRenderer = gameObject.AddComponent<MeshRenderer>();
                _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _meshRenderer.receiveShadows = false;
                _meshRenderer.hideFlags = HideFlags.HideInInspector;
            }

            if( !gameObject.GetComponent<MeshCollider>())
            {
                _meshCollider = gameObject.AddComponent<MeshCollider>();
                _meshCollider.convex = false;
                _meshCollider.sharedMesh = _sharedMesh;
                _meshCollider.hideFlags = HideFlags.HideInInspector;
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
                _meshRenderer.sharedMaterials = _Materials;

                _meshRenderer.sharedMaterials[0].hideFlags = HideFlags.HideInInspector;
                _meshRenderer.sharedMaterials[1].hideFlags = HideFlags.HideInInspector;
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

        public void SelectAll()
        {
            if (_sharedMesh == null)
                return;

            selectedTriangles.SetAll(true);

            UpdateSelectionMesh();
        }

        public void ClearAll()
        {
            selectedTriangles.SetAll(false);

            UpdateSelectionMesh();
        }

        public void UpdateSelectionMesh()
        {
            int selectedCount = UMAUtils.GetCardinality(selectedTriangles);
            int[] newSelectedTriangles = new int[selectedCount];
            int selectedIndex = 0;

            for (int i = 0; i < selectedTriangles.Length; i+=3)
            {                
                if (selectedTriangles[i])
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
                Gizmos.DrawWireMesh(_sharedMesh, gameObject.transform.position, gameObject.transform.rotation);
        }
    }
}
