using UnityEngine;
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

        void OnDrawGizmosSelected()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            EditorUtility.SetSelectedWireframeHidden(GetComponent<Renderer>(), !_displayWireframe);
#pragma warning restore CS0618 // Type or member is obsolete
            OnDrawNormals(true);
        }

        void OnDrawGizmos()
        {
            if (!Selection.Contains(this))
            {
                OnDrawNormals(false);
            }
        }


        public Mesh mesh;
        private void OnDrawNormals(bool isSelected)
        {

            if (_skinnedMesh == null)
            {
                _skinnedMesh = GetComponent<SkinnedMeshRenderer>();
                if (_skinnedMesh == null)
                {
                    return;
                }
            }

            if (mesh == null && _skinnedMesh != null)
            {
                mesh = new Mesh();
                _skinnedMesh.BakeMesh(mesh, true);
            }

            if (mesh == null)
            { return; }

            //Draw Vertex Normals
            if (_vertexNormals.CanDraw(isSelected))
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                for (int i = 0; i < vertices.Length; i++)
                {
                    _vertexNormals.Draw(transform.TransformPoint(vertices[i]), transform.TransformVector(normals[i]));
                }
            }
        }
#endif
    }
}
