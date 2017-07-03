using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

namespace UMA.Editors
{
    //OnPreviewGUI
    //http://timaksu.com/post/126337219047/spruce-up-your-custom-unity-inspectors-with-a
    //
    [CustomEditor(typeof(MeshHideAsset))]
    public class MeshHideInspector : Editor 
    {
        private Mesh _meshPreview;
        private UMAMeshData _meshData;
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
                ResetPreviewCamera();
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

            string info;
            if (source.TriangleCount > 0)
            {
                info = "Triangle indices Count: " + source.TriangleCount.ToString();
                info += "\nSubmesh Count: " + source.SubmeshCount.ToString();
                info += "\nHidden Triangle Count: " + source.HiddenCount.ToString();
            }
            else
                info = "No triangle array found";

            EditorGUILayout.HelpBox(info, MessageType.Info);

            if (GUILayout.Button("Begin Editing"))
            {
                CreateSceneEditObject();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void UpdateMeshPreview()
        {
            MeshHideAsset source = target as MeshHideAsset;

            if( _meshPreview == null )
                _meshPreview = new Mesh();

            UpdateMeshData( source.triangleFlags);

            _meshPreview.Clear();
            _meshPreview.vertices = _meshData.vertices;
            _meshPreview.SetTriangles(_meshData.submeshes[0].triangles, 0); //temp for only first submesh

            ResetPreviewCamera();
        }

        public void UpdateMeshData( BitArray[] triangleFlags )
        {
            if (triangleFlags == null)
            {
                Debug.LogWarning("UpdateMeshData: triangleFlags are null!");
                return;
            }

            if (_meshData == null )
                _meshData = new UMAMeshData();

            MeshHideAsset source = target as MeshHideAsset;

            UMAMeshData sourceData = source.asset.meshData;
                
            _meshData.submeshes = new SubMeshTriangles[sourceData.subMeshCount];
            _meshData.subMeshCount = sourceData.subMeshCount;

            bool has_normals = (sourceData.normals != null && sourceData.normals.Length != 0);

            _meshData.vertices = new Vector3[sourceData.vertexCount];
            sourceData.vertices.CopyTo(_meshData.vertices, 0);

            if(has_normals)
            {
                _meshData.normals = new Vector3[sourceData.vertexCount];
                sourceData.normals.CopyTo(_meshData.normals, 0);
            }

            for (int i = 0; i < sourceData.subMeshCount; i++)
            {
                List<int> newTriangles = new List<int>();
                for (int j = 0; j < sourceData.submeshes[i].triangles.Length; j++)
                {
                    if (!triangleFlags[i][j])
                        newTriangles.Add(sourceData.submeshes[i].triangles[j]);
                }
                _meshData.submeshes[i] = new SubMeshTriangles();
                _meshData.submeshes[i].triangles = new int[newTriangles.Count];
                newTriangles.CopyTo(_meshData.submeshes[i].triangles);
            }
        }

        private void CreateSceneEditObject()
        {
            GameObject obj = new GameObject();
            GeometrySelector geometry = obj.AddComponent<GeometrySelector>();
            MeshHideAsset source = target as MeshHideAsset;

            if (geometry != null)
            {
                geometry.meshAsset = source;
                geometry.doneEditing += source.SaveSelection;
                geometry.InitializeFromMeshData(source.asset.meshData);
                Selection.activeGameObject = obj;

                //temporary, only works on submesh 0
                geometry.selectedTriangles = new BitArray(source.triangleFlags[0]);

                geometry.UpdateSelectionMesh();
            }
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
            if (_meshPreview == null)
                return;

            _drag = Drag2D(_drag, r);

            if (_previewRenderUtility == null)
            {
                _previewRenderUtility = new PreviewRenderUtility();
                ResetPreviewCamera();
            }

            if( Event.current.type == EventType.repaint )
            {
                _previewRenderUtility.BeginPreview(r, background);
                _previewRenderUtility.DrawMesh(_meshPreview, Vector3.zero, Quaternion.identity, _material, 0);

                _previewRenderUtility.m_Camera.transform.position = Vector2.zero;
                _previewRenderUtility.m_Camera.transform.rotation = Quaternion.Euler(new Vector3(-_drag.y, -_drag.x, 0));
                _previewRenderUtility.m_Camera.transform.position = _previewRenderUtility.m_Camera.transform.forward * -6f;
                _previewRenderUtility.m_Camera.transform.position +=  _meshPreview.bounds.center;

                _previewRenderUtility.m_Camera.Render();

                Texture resultRender = _previewRenderUtility.EndPreview();

                GUI.DrawTexture(r, resultRender, ScaleMode.StretchToFill, false);
            }
        }

        public void ResetPreviewCamera()
        {
            if (_previewRenderUtility == null)
                return;

            MeshHideAsset source = target as MeshHideAsset;
            
            _drag = Vector2.zero;
            if( source.asset.meshData.rootBoneHash == UMAUtils.StringToHash("Global"))
                _drag.y = -90;
            
            _previewRenderUtility.m_Camera.transform.position = new Vector3(0, 0, -6);
            _previewRenderUtility.m_Camera.transform.rotation = Quaternion.identity;
        }

        public override void OnPreviewSettings()
        {
            if (GUILayout.Button("Refresh", EditorStyles.whiteMiniLabel))
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
