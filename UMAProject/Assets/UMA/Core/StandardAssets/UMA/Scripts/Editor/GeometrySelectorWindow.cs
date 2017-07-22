using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA.Editors
{
    public class GeometrySelectorWindow : EditorWindow {

        private GeometrySelector _Source;
        private bool doneEditing = false; //set to true to end editing this objects
        private bool showWireframe = true; //whether to switch to wireframe mode or not
        private bool backfaceCull = true; 
        private bool isSelecting = false; //is the user actively selecting
        private bool setSelectedOn = true; //whether to set the triangles to selected or unselection when using selection box
        private Vector2 startMousePos;
        private Texture2D textureMap;

        private const float drawTolerance = 10.0f; //in pixels
        private Color selectionColor = new Color(0.8f, 0.8f, 0.95f, 0.15f);

        public static void Init(GeometrySelector source)
        {
            GeometrySelectorWindow window = (GeometrySelectorWindow)EditorWindow.GetWindow(typeof(GeometrySelectorWindow));
            window._Source = source;
            window.minSize = new Vector2(200, 400);
            window.Show();
        }

        void OnEnable()
        {
            EditorApplication.update += GeometryUpdate;
            SceneView.onSceneGUIDelegate += OnSceneGUI;
            UpdateShadingMode(showWireframe);
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            EditorApplication.update -= GeometryUpdate;
            SceneView.onSceneGUIDelegate -= OnSceneGUI;
            DestroySceneEditObject();
        }

        void OnGUI()
        {
            GUILayout.Space(20);
            EditorGUILayout.LabelField("Visual Options");
            GUILayout.BeginHorizontal();
            bool toggled = GUILayout.Toggle(showWireframe, new GUIContent("Show Wireframe", "Toggle showing the Wireframe"), "Button", GUILayout.MinHeight(50));
            if (toggled != showWireframe) { UpdateShadingMode(toggled); }           
            showWireframe = toggled;

            backfaceCull = GUILayout.Toggle(backfaceCull, new GUIContent("  Backface Cull  ", "Toggle whether to select back faces"), "Button", GUILayout.MinHeight(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Selection Options");
            GUILayout.BeginHorizontal();
            setSelectedOn = GUILayout.Toggle(setSelectedOn, new GUIContent("Unselect", "Toggle to apply unselected state to triangles highlighted"), "Button", GUILayout.MinHeight(50));
            setSelectedOn = GUILayout.Toggle(!setSelectedOn, new GUIContent("  Select  ", "Toggle to apply selected state to triangles highlighted"), "Button", GUILayout.MinHeight(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All", GUILayout.MinHeight(50)))
            {
                ClearAll();
            }

            if (GUILayout.Button("Select All", GUILayout.MinHeight(50)))
            {
                SelectAll();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            textureMap = EditorGUILayout.ObjectField("Set From Texture Map", textureMap, typeof(Texture2D), false) as Texture2D;                
            if (GUILayout.Button("Load Texture Map"))
            {
                if(_Source != null)
                    _Source.UpdateFromTexture(textureMap);                
            }

            GUILayout.Space(20);
            if (GUILayout.Button(new GUIContent("Done Editing", "Save the changes and apply them to the MeshHideAsset"), GUILayout.MinHeight(50)))
            {
                doneEditing = true;
            }
        }

        private void UpdateShadingMode(bool wireframeOn)
        {
            if (SceneView.lastActiveSceneView == null)
            {
                Debug.LogWarning("currentDrawingSceneView is null");
                return;
            }

            if (wireframeOn)
                SceneView.lastActiveSceneView.renderMode = DrawCameraMode.TexturedWire;
            else
                SceneView.lastActiveSceneView.renderMode = DrawCameraMode.Textured;

            SceneView.RepaintAll();
        }

        private void ClearAll()
        {
            if (_Source != null)
                _Source.ClearAll();
        }

        private void SelectAll()
        {
            if (_Source != null)
                _Source.SelectAll();
        }

        private void GeometryUpdate()
        {
            if (doneEditing)
                Cleanup();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (_Source == null)
                return;
            
            Rect selectionRect = new Rect();

            if (Event.current.type == EventType.layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode(), FocusType.Passive));

            if (isSelecting)
            {
                Vector2 selectionSize = (Event.current.mousePosition - startMousePos);
                Vector2 correctedPos = startMousePos;
                if (selectionSize.x < 0)
                {
                    selectionSize.x = Mathf.Abs(selectionSize.x);
                    correctedPos.x = startMousePos.x - selectionSize.x;
                }
                if (selectionSize.y < 0)
                {
                    selectionSize.y = Mathf.Abs(selectionSize.y);
                    correctedPos.y = startMousePos.y - selectionSize.y;
                }
                if (selectionSize.x > drawTolerance || selectionSize.y > drawTolerance)
                {
                    Handles.BeginGUI();
                    selectionRect = new Rect(correctedPos, selectionSize);
                    Handles.DrawSolidRectangleWithOutline(selectionRect, selectionColor, Color.black);
                    Handles.EndGUI();
                    HandleUtility.Repaint();
                }
            }

            if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                isSelecting = true;
                startMousePos = Event.current.mousePosition;

                int[] triangleHit = RayPick();
                if (triangleHit != null)
                {
                    _Source.selectedTriangles[triangleHit[0]] = !_Source.selectedTriangles[triangleHit[0]];
                    _Source.selectedTriangles[triangleHit[1]] = !_Source.selectedTriangles[triangleHit[1]];
                    _Source.selectedTriangles[triangleHit[2]] = !_Source.selectedTriangles[triangleHit[2]];

                    _Source.UpdateSelectionMesh();
                }
            }

            if (Event.current != null && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (isSelecting)
                {
                    isSelecting = false;

                    int[] triangles = _Source.meshAsset.asset.meshData.submeshes[0].triangles;
                    for(int i = 0; i < triangles.Length; i+=3 )
                    {
                        bool found = false;
                        Vector3 center = new Vector3();
                        Vector3 centerNormal = new Vector3();

                        for (int k = 0; k < 3; k++)
                        {
                            Vector3 vertex = _Source.meshAsset.asset.meshData.vertices[triangles[i+k]];
                            vertex = _Source.transform.localToWorldMatrix.MultiplyVector(vertex);

                            Vector3 normal = _Source.meshAsset.asset.meshData.normals[triangles[i+k]];
                            normal = _Source.transform.localToWorldMatrix.MultiplyVector(normal);

                            center += vertex;
                            centerNormal += normal;

                            vertex = SceneView.currentDrawingSceneView.camera.WorldToScreenPoint(vertex);
                            vertex.y = SceneView.currentDrawingSceneView.camera.pixelHeight - vertex.y;

                            if (selectionRect.Contains( vertex ))
                            {
                                if (backfaceCull)
                                {
                                    if (Vector3.Dot(normal, SceneView.currentDrawingSceneView.camera.transform.forward) < -0.5f)
                                        found = true;
                                }
                                else
                                    found = true;
                            }
                        }

                        center = center / 3;
                        centerNormal = centerNormal / 3;
                        center = SceneView.currentDrawingSceneView.camera.WorldToScreenPoint(center);
                        center.y = SceneView.currentDrawingSceneView.camera.pixelHeight - center.y;
                        if (selectionRect.Contains(center))
                        {
                            if (backfaceCull)
                            {
                                if (Vector3.Dot(centerNormal, SceneView.currentDrawingSceneView.camera.transform.forward) < -0.5f)
                                    found = true;
                            }
                            else
                                found = true;
                        }

                        if (found)
                        {
                            _Source.selectedTriangles[i] = setSelectedOn;
                            _Source.selectedTriangles[i+1] = setSelectedOn;
                            _Source.selectedTriangles[i+2] = setSelectedOn;
                        }
                    }

                    _Source.UpdateSelectionMesh();
                }
            }

            if (Event.current.type == EventType.MouseMove)
            {
                SceneView.RepaintAll();
            }
        }

        private int[] RayPick()
        {
            if (Camera.current == null)
            {
                Debug.LogWarning("Camera is null!");
                return null;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit))
                return null;

            MeshCollider meshCollider = hit.collider as MeshCollider;
            if (meshCollider == null || meshCollider.sharedMesh == null || meshCollider != _Source.meshCollider)
                return null;

            int[] triangle = new int[3];
            triangle[0] = hit.triangleIndex * 3 + 0;
            triangle[1] = hit.triangleIndex * 3 + 1;
            triangle[2] = hit.triangleIndex * 3 + 2;

            return triangle;
        }

        private void DestroySceneEditObject()
        {
            if (_Source != null)
            {
                UpdateShadingMode(false);
                if (_Source.doneEditing != null)
                    _Source.doneEditing(_Source.selectedTriangles);
                if (_Source.meshAsset != null)
                    Selection.activeObject = _Source.meshAsset;
                DestroyImmediate(_Source.gameObject);

                this.Close();
            }
        }
    }
}
