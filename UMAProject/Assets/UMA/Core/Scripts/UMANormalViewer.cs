using UnityEngine;
using UMA.CharacterSystem;


#if UNITY_EDITOR
using UnityEditor;
#endif
namespace UMA
{

    public class UMANormalViewer : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField]
        private SkinnedMeshRenderer _skinnedMesh = null;
        [SerializeField]
        private bool _displayWireframe = false;
        [SerializeField]
        private NormalsDrawData _vertexNormals = new NormalsDrawData(new Color32(200, 0, 0, 240), false);

        public DynamicCharacterAvatar avatar;
        private UMAData _umaData;
    private bool _meshBakeValid;
    private SkinnedMeshRenderer _bakedSkinnedMesh;

        [System.Serializable]
        private class NormalsDrawData
        {
            [SerializeField]
            protected DrawType _draw = DrawType.Selected;
            protected enum DrawType { Never, Selected, Always }
            [SerializeField]
            protected float _length = 0.035f;
            [SerializeField]
            protected Color _normalColor;
            private Color _baseColor = new Color32(255, 133, 0, 255);
            public float vertexCircumference = 0.0125f;
            public bool showVertexes = false;


            public NormalsDrawData(Color normalColor, bool draw)
            {
                _normalColor = normalColor;
                _draw = draw ? DrawType.Selected : DrawType.Never;
            }

            public bool CanDraw(bool isSelected)
            {
                return (_draw == DrawType.Always) || (_draw == DrawType.Selected && isSelected);
            }

            public void Draw(Vector3 from, Vector3 direction)
            {
                if (Camera.current.transform.InverseTransformDirection(direction).z < 0f)
                {
                    if (showVertexes)
                    {
                        Gizmos.color = _baseColor;
                        Gizmos.DrawWireSphere(from, vertexCircumference);
                    }
                    Gizmos.color = _normalColor;
                    Gizmos.DrawRay(from, direction * _length);
                }
            }
        }


        private void Start()
        {
            SubscribeToAvatar();
        }

        private void OnEnable()
        {
            SubscribeToAvatar();
        }

        private void OnDisable()
        {
            UnsubscribeFromAvatar();
        }

        private void OnDestroy()
        {
            DestroyBakedMesh();
        }

        private void SubscribeToAvatar()
        {
            UnsubscribeFromAvatar();

            _umaData = FindUMAData();
            if (_umaData != null)
            {
                _umaData.OnCharacterUpdated += Avatar_OnCharacterUpdated;
            }
        }

        private void UnsubscribeFromAvatar()
        {
            if (_umaData != null)
            {
                _umaData.OnCharacterUpdated -= Avatar_OnCharacterUpdated;
                _umaData = null;
            }
        }

        private UMAData FindUMAData()
        {
            if (avatar != null)
            {
                return avatar.umaData;
            }

            avatar = GetComponent<DynamicCharacterAvatar>();
            if (avatar == null)
            {
                avatar = GetComponentInParent<DynamicCharacterAvatar>();
            }

            if (avatar == null)
            {
                avatar = GetComponentInChildren<DynamicCharacterAvatar>(true);
            }

            if (avatar != null)
            {
                return avatar.umaData;
            }

            UMAData umaData = GetComponent<UMAData>();
            if (umaData == null)
            {
                umaData = GetComponentInParent<UMAData>();
            }

            if (umaData == null)
            {
                umaData = GetComponentInChildren<UMAData>(true);
            }

            return umaData;
        }

        private void Avatar_OnCharacterUpdated(UMAData obj)
        {
            InvalidateBakedMesh();
        }

        private void InvalidateBakedMesh()
        {
            _meshBakeValid = false;
            _bakedSkinnedMesh = null;
            _skinnedMesh = null;
        }

        private void DestroyBakedMesh()
        {
            if (mesh != null)
            {
                DestroyImmediate(mesh);
                mesh = null;
            }

            InvalidateBakedMesh();
        }

        void OnDrawGizmosSelected()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            EditorUtility.SetSelectedWireframeHidden(GetComponent<Renderer>(), !_displayWireframe);
#pragma warning restore CS0618 // Type or member is obsolete
            OnDrawNormals(true);
        }

        void OnDrawGizmos()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!Selection.Contains(this))
            {
                OnDrawNormals(false);
            }
        }


        public Mesh mesh;
        private void OnDrawNormals(bool isSelected)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (Application.isPlaying && !EditorApplication.isPaused)
            {
                return;
            }

            if (!EnsureSkinnedMesh())
            {
                return;
            }

            if (ShouldBakeMesh())
            {
                BakeMesh();
            }

            if (mesh == null)
            { return; }

            //Draw Vertex Normals
            if (_vertexNormals.CanDraw(isSelected))
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Transform rendererTransform = _bakedSkinnedMesh != null ? _bakedSkinnedMesh.transform : _skinnedMesh.transform;
                int normalCount = Mathf.Min(vertices.Length, normals.Length);
                for (int i = 0; i < normalCount; i++)
                {
                    Vector3 normal = rendererTransform.TransformDirection(normals[i]).normalized;
                    if (normal.sqrMagnitude > 0f)
                    {
                        _vertexNormals.Draw(rendererTransform.TransformPoint(vertices[i]), normal);
                    }
                }
            }
        }

        private bool EnsureSkinnedMesh()
        {
            SkinnedMeshRenderer currentRenderer = FindSkinnedMeshRenderer();
            if (currentRenderer == null)
            {
                return false;
            }

            if (_skinnedMesh != currentRenderer)
            {
                _skinnedMesh = currentRenderer;
                _meshBakeValid = false;
                _bakedSkinnedMesh = null;
            }

            return true;
        }

        private SkinnedMeshRenderer FindSkinnedMeshRenderer()
        {
            if (_umaData == null)
            {
                _umaData = FindUMAData();
            }

            SkinnedMeshRenderer umaRenderer = GetFirstUMARenderer(true);
            if (umaRenderer != null)
            {
                return umaRenderer;
            }

            umaRenderer = GetFirstUMARenderer(false);
            if (umaRenderer != null)
            {
                return umaRenderer;
            }

            if (_skinnedMesh != null && _skinnedMesh.sharedMesh != null)
            {
                return _skinnedMesh;
            }

            return GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        private SkinnedMeshRenderer GetFirstUMARenderer(bool requireEnabled)
        {
            if (_umaData == null)
            {
                return null;
            }

            SkinnedMeshRenderer[] renderers = _umaData.GetRenderers();
            if (renderers == null)
            {
                return null;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                if (requireEnabled && !renderer.enabled)
                {
                    continue;
                }

                return renderer;
            }

            return null;
        }

        private bool ShouldBakeMesh()
        {
            if (!_meshBakeValid || mesh == null || _bakedSkinnedMesh != _skinnedMesh)
            {
                return true;
            }

            return !Application.isPlaying || EditorApplication.isPaused;
        }

        private void BakeMesh()
        {
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "UMANormalViewer Baked Mesh",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            else
            {
                mesh.Clear();
            }

            _skinnedMesh.BakeMesh(mesh, false);
            _bakedSkinnedMesh = _skinnedMesh;
            _meshBakeValid = true;
        }
#endif
    }
}
