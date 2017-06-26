using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA.Editors
{
    //OnPreviewGUI
    //http://timaksu.com/post/126337219047/spruce-up-your-custom-unity-inspectors-with-a
    //
    [CustomEditor(typeof(MeshHideAsset))]
    public class MeshHideInspector : Editor 
    {
        private Mesh _meshPreview;
        private PreviewRenderUtility _previewRenderUtility;
        private Vector2 _drag;
        private Material _material;

        void OnEnable()
        {
            MeshHideAsset source = target as MeshHideAsset;
            if (source.asset == null)
                return;

            if (_meshPreview == null)
            {
                UpdateMeshPreview();
            }

            if (_previewRenderUtility == null)
            {
                _previewRenderUtility = new PreviewRenderUtility();
                _previewRenderUtility.m_Camera.transform.position = new Vector3(0, 0, -6);
                _previewRenderUtility.m_Camera.transform.rotation = Quaternion.identity;
            }

            if( _material == null )
                _material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MeshHideAsset source = target as MeshHideAsset;

            //DrawDefaultInspector();
            if (source.asset == null)
                EditorGUILayout.HelpBox("No SlotDataAsset set!", MessageType.Warning);

            var obj = EditorGUILayout.ObjectField("SlotDataAsset", source.asset, typeof(SlotDataAsset), false);
            if (obj != null && obj != source.asset)
            {
                source.asset = obj as SlotDataAsset;
                source.Initialize();
                UpdateMeshPreview();
                AssetDatabase.SaveAssets();
                EditorUtility.SetDirty(target);
            }

            string vertexInfo;
            if (source.VertexCount > 0)
            {
                vertexInfo = "Vertex Count: " + source.VertexCount.ToString();
                vertexInfo += "\nHidden Vertices: " + source.NumHiddenVertices().ToString();
            }
            else
                vertexInfo = "No vertex array found";

            EditorGUILayout.HelpBox(vertexInfo, MessageType.Info);

            if (GUILayout.Button("Create Scene Object"))
            {
                CreateSceneEditObject();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void UpdateMeshPreview()
        {
            _meshPreview = new Mesh();
            
            MeshHideAsset source = target as MeshHideAsset;

            UMAMeshData _meshData = MeshHideAsset.FilterMeshData( source.asset.meshData, source.vertexFlags);
            if (_meshData == null)
                return;

            _meshPreview.vertices = _meshData.vertices;
            _meshPreview.triangles = _meshData.submeshes[0].triangles;
        }

        private void CreateSceneEditObject()
        {
            GameObject obj = new GameObject();
            obj.name = "MeshHideEditObject";
            MeshHideEditObject meshHide = obj.AddComponent<MeshHideEditObject>();
            meshHide.pickCollider = obj.AddComponent<BoxCollider>(); //for object picking

            meshHide.HideAsset = target as MeshHideAsset;
            Selection.activeGameObject = obj;

            meshHide.pickCollider.size = new Vector3( 0.01f, 0.01f, 0.01f);
            meshHide.pickCollider.center = new Vector3(0, 0, 0);
        }

        public override bool HasPreviewGUI()
        {
            MeshHideAsset source = target as MeshHideAsset;
            if (source.asset == null)
                return false;
            
            return true;
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            _drag = Drag2D(_drag, r);

            if (_previewRenderUtility == null)
            {
                _previewRenderUtility = new PreviewRenderUtility();
                _previewRenderUtility.m_Camera.transform.position = new Vector3(0, 0, -6);
                _previewRenderUtility.m_Camera.transform.rotation = Quaternion.identity;
            }

            if( Event.current.type == EventType.repaint )
            {
                _previewRenderUtility.BeginPreview(r, background);
                _previewRenderUtility.DrawMesh(_meshPreview, Matrix4x4.identity, _material, 0);

                _previewRenderUtility.m_Camera.transform.position = Vector2.zero;
                _previewRenderUtility.m_Camera.transform.rotation = Quaternion.Euler(new Vector3(-_drag.y, -_drag.x, 0));
                _previewRenderUtility.m_Camera.transform.position = _previewRenderUtility.m_Camera.transform.forward * -6f;
                _previewRenderUtility.m_Camera.transform.position +=  _meshPreview.bounds.center;

                _previewRenderUtility.m_Camera.Render();

                Texture resultRender = _previewRenderUtility.EndPreview();

                GUI.DrawTexture(r, resultRender, ScaleMode.StretchToFill, false);
            }
        }

        public override void OnPreviewSettings()
        {
            if (GUILayout.Button("Update Mesh", EditorStyles.whiteMiniLabel))
                UpdateMeshPreview();
        }

        void OnDestroy()
        {
            if( _previewRenderUtility != null )
                _previewRenderUtility.Cleanup();
        }

        public static Vector2 Drag2D(Vector2 scrollPosition, Rect position)
        {
            int controlID = GUIUtility.GetControlID("Slider".GetHashCode(), FocusType.Passive);
            Event current = Event.current;
            switch (current.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (position.Contains(current.mousePosition) && position.width > 50f)
                    {
                        GUIUtility.hotControl = controlID;
                        current.Use();
                        EditorGUIUtility.SetWantsMouseJumping(1);
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID)
                    {
                        GUIUtility.hotControl = 0;
                    }
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID)
                    {
                        scrollPosition -= current.delta * (float)((!current.shift) ? 1 : 3) / Mathf.Min(position.width, position.height) * 140f;
                        scrollPosition.y = Mathf.Clamp(scrollPosition.y, -90f, 90f);
                        current.Use();
                        GUI.changed = true;
                    }
                    break;
            }
            return scrollPosition;
        }
    }
}
