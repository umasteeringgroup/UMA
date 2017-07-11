using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    [CustomEditor(typeof(GeometrySelector))]
    public class GeometrySelectorInspector : Editor
    {
        private bool doneEditing = false; //set to true to end editing this objects
        private bool showWireframe = true; //whether to switch to wireframe mode or not
        private bool backfaceCull = true; 
        private bool isSelecting = false; //is the user actively selecting
        private bool setSelectedOn = true; //whether to set the triangles to selected or unselection when using selection box
        private Vector2 startMousePos;
        private Rect screenRect; 
        private Texture2D textureMap;

        private float drawTolerance = 10.0f; //in pixels

        #region Selection Utility Class
        // Credit: http://hyunkell.com/blog/rts-style-unit-selection-in-unity-5/
        public class SelectionUtils
        {
            static Texture2D _whiteTexture;
            public static Texture2D WhiteTexture
            {
                get
                {
                    if (_whiteTexture == null)
                    {
                        _whiteTexture = new Texture2D(1, 1);
                        _whiteTexture.SetPixel(0, 0, Color.white);
                        _whiteTexture.Apply();
                    }

                    return _whiteTexture;
                }
            }

            public static void DrawScreenRect( Rect rect, Color color )
            {
                GUI.color = color;
                GUI.Box(rect, WhiteTexture);
                GUI.color = Color.white;
            }
        }
        #endregion

        void OnEnable()
        {
            EditorApplication.update += GeometryUpdate;

            UpdateShadingMode(showWireframe);
        }

        public override void OnInspectorGUI()
        {
            GeometrySelector source = target as GeometrySelector;

            //base.OnInspectorGUI();
            serializedObject.Update();

            var obj = EditorGUILayout.ObjectField("SharedMesh", source.sharedMesh, typeof(Mesh), false);
            if (obj != null && obj != source.sharedMesh)
            {
                source.sharedMesh = obj as Mesh;
                EditorUtility.SetDirty(target);
            }

            GUILayout.BeginHorizontal();
            bool toggled = GUILayout.Toggle(showWireframe, new GUIContent("Show Wireframe", "Toggles showing the Wireframe"), "Button", GUILayout.MinHeight(50));
            if (toggled != showWireframe) { UpdateShadingMode(toggled); }           
            showWireframe = toggled;

            backfaceCull = GUILayout.Toggle(backfaceCull, new GUIContent("  Backface Cull  ", "Toggles whether to select back faces"), "Button", GUILayout.MinHeight(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            setSelectedOn = GUILayout.Toggle(setSelectedOn, new GUIContent("Unselect", "Toggles to apply unselected state to triangles highlighted"), "Button", GUILayout.MinHeight(50));
            setSelectedOn = GUILayout.Toggle(!setSelectedOn, new GUIContent("  Select  ", "Toggles to apply selected state to triangles highlighted"), "Button", GUILayout.MinHeight(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All", GUILayout.MinHeight(50)))
            {
                source.ClearAll();
            }

            if (GUILayout.Button("Select All", GUILayout.MinHeight(50)))
            {
                source.SelectAll();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            textureMap = EditorGUILayout.ObjectField("Texture Map", textureMap, typeof(Texture2D), false) as Texture2D;                
            if( GUILayout.Button("Load Texture Map"))
                source.UpdateFromTexture(textureMap);                

            GUILayout.Space(20);
            if (GUILayout.Button(new GUIContent("Done Editing", "Save the changes and apply them to the MeshHideAsset"), GUILayout.MinHeight(50)))
            {
                doneEditing = true;
            }
            serializedObject.ApplyModifiedProperties();
        }

        void GeometryUpdate()
        {
            if (doneEditing)
            {
                EditorApplication.update -= GeometryUpdate;
                DestroySceneEditObject();
            }
        }

        void OnSceneGUI()
        {
            GeometrySelector source = target as GeometrySelector;
            Rect selectionRect = new Rect();

            screenRect = new Rect(0, 0, Camera.current.pixelWidth, Camera.current.pixelHeight);

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
                    GUILayout.BeginArea(screenRect);

                    selectionRect = new Rect(correctedPos, selectionSize);
                    SelectionUtils.DrawScreenRect(selectionRect, new Color(0.8f, 0.8f, 0.95f, 0.25f));

                    GUILayout.EndArea();
                    SceneView.RepaintAll();
                }
            }

            if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                isSelecting = true;
                startMousePos = Event.current.mousePosition;

                int[] triangleHit = RayPick();
                if (triangleHit != null)
                {
                    source.selectedTriangles[triangleHit[0]] = !source.selectedTriangles[triangleHit[0]];
                    source.selectedTriangles[triangleHit[1]] = !source.selectedTriangles[triangleHit[1]];
                    source.selectedTriangles[triangleHit[2]] = !source.selectedTriangles[triangleHit[2]];

                    source.UpdateSelectionMesh();
                }
            }

            if (Event.current != null && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (isSelecting)
                {
                    isSelecting = false;

                    int[] triangles = source.meshAsset.asset.meshData.submeshes[0].triangles;
                    for(int i = 0; i < triangles.Length; i+=3 )
                    {
                        bool found = false;
                        Vector3 center = new Vector3();
                        Vector3 centerNormal = new Vector3();

                        for (int k = 0; k < 3; k++)
                        {
                            Vector3 vertex = source.meshAsset.asset.meshData.vertices[triangles[i+k]];
                            vertex = source.transform.localToWorldMatrix.MultiplyVector(vertex);

                            Vector3 normal = source.meshAsset.asset.meshData.normals[triangles[i+k]];
                            normal = source.transform.localToWorldMatrix.MultiplyVector(normal);

                            center += vertex;
                            centerNormal += normal;

                            vertex = SceneView.currentDrawingSceneView.camera.WorldToScreenPoint(vertex);
                            vertex.y = SceneView.currentDrawingSceneView.camera.pixelHeight - vertex.y;

                            if (selectionRect.Contains( vertex ))
                            {
                                if (backfaceCull)
                                {
                                    //if (Vector3.Dot(normal, SceneView.currentDrawingSceneView.camera.transform.position - normal) > 0.1f)
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
                                //if (Vector3.Dot(centerNormal, SceneView.currentDrawingSceneView.camera.transform.position - centerNormal) > 0.1f)
                                if (Vector3.Dot(centerNormal, SceneView.currentDrawingSceneView.camera.transform.forward) < -0.5f)
                                    found = true;
                            }
                            else
                                found = true;
                        }

                        if (found)
                        {
                            source.selectedTriangles[i] = setSelectedOn;
                            source.selectedTriangles[i+1] = setSelectedOn;
                            source.selectedTriangles[i+2] = setSelectedOn;
                        }
                    }

                    source.UpdateSelectionMesh();
                }
            }

            if (Event.current.type == EventType.MouseMove)
            {
                SceneView.RepaintAll();
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

        private int[] RayPick()
        {
            GeometrySelector source = target as GeometrySelector;

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
            if (meshCollider == null || meshCollider.sharedMesh == null || meshCollider != source.meshCollider)
                return null;

            Mesh mesh = meshCollider.sharedMesh;

            int[] triangle = new int[3];
            triangle[0] = hit.triangleIndex * 3 + 0;
            triangle[1] = hit.triangleIndex * 3 + 1;
            triangle[2] = hit.triangleIndex * 3 + 2;

            return triangle;
        }

        private void DestroySceneEditObject()
        {
            GeometrySelector source = target as GeometrySelector;
            UpdateShadingMode(false);
            if( source.doneEditing != null) source.doneEditing( source.selectedTriangles );
            if(source.meshAsset != null) 
                Selection.activeObject = source.meshAsset;
            DestroyImmediate(source.gameObject);
        }
    }
}
