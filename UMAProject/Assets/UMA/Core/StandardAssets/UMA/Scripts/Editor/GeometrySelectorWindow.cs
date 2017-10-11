using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

namespace UMA.Editors
{
    public class GeometrySelectorWindow : EditorWindow {

        private GeometrySelector _Source;
		private SlotDataAsset _Occluder = null;
        private float _occluderOffset = 0;
        private Vector3 _occluderRotation = Vector3.zero;
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
            EditorGUILayout.LabelField(new GUIContent("Occlusion Slot (Optional)","Use this mesh to attempt to automatically detect occluded triangles"));
			SlotDataAsset newOccluder = (SlotDataAsset) EditorGUILayout.ObjectField(_Occluder, typeof(SlotDataAsset), false);
			if (newOccluder != _Occluder)
			{
				_Occluder = newOccluder;
				if (_Occluder != null)
                    _Source.CreateOcclusionMesh(_Occluder.meshData);
				else
					_Source.occlusionMesh = null;
			}

            if (_Occluder != null)
            {
                bool newOffset = false;
                bool newRot = false;
                float previousOffset = EditorGUILayout.FloatField(new GUIContent("Occluder Offset", "Distance along the normal to offset each vertex of the occlusion mesh"), _occluderOffset);
                if (!Mathf.Approximately(previousOffset,_occluderOffset))
                {
                    _occluderOffset = previousOffset;
                    newOffset = true;
                }
                Vector3 previousRot = EditorGUILayout.Vector3Field(new GUIContent("Rotation", "Offset the rotation (degrees) of the occluder"), _occluderRotation );
                if (previousRot != _occluderRotation)
                {
                    _occluderRotation = previousRot;
                    newRot = true;
                }

                if (newOffset || newRot)
                    _Source.UpdateOcclusionMesh(_occluderOffset, _occluderRotation);

                if (GUILayout.Button(new GUIContent("Raycast Hidden Faces", "Warning! This will clear the current selection.")))
                {
                    RaycastHide();
                }
            }

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Visual Options");
            GUILayout.BeginHorizontal();
            bool toggled = GUILayout.Toggle(showWireframe, new GUIContent("Show Wireframe", "Toggle showing the Wireframe"), "Button", GUILayout.MinHeight(50));
            if (toggled != showWireframe) { UpdateShadingMode(toggled); }           
            showWireframe = toggled;

            backfaceCull = GUILayout.Toggle(backfaceCull, new GUIContent("  Backface Cull  ", "Toggle whether to select back faces"), "Button", GUILayout.MinHeight(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Selection Options (Drag to area select, hold shift to paint selection");
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
                Close();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (_Source == null)
                return;
			
			if (!isSelecting && Event.current.alt)
				return;

			if (!isSelecting && Event.current.control)
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

                if (Event.current.shift)
                {
                    startMousePos = Event.current.mousePosition;

                    int triangleHit = RayPick();
                    if (triangleHit >= 0)
                    {
                        if (_Source.selectedTriangles[triangleHit] != setSelectedOn)
                        {
                            // avoid constant rebuild.
                            _Source.selectedTriangles[triangleHit] = setSelectedOn;
                            _Source.UpdateSelectionMesh();
                        }
                    }
                }
                else if (selectionSize.x > drawTolerance || selectionSize.y > drawTolerance)
                {
                    Handles.BeginGUI();
                    selectionRect = new Rect(correctedPos, selectionSize);
                    Handles.DrawSolidRectangleWithOutline(selectionRect, selectionColor, Color.black);
                    Handles.EndGUI();
                    HandleUtility.Repaint();
                }

				if (Event.current.type == EventType.MouseDrag)
				{
					SceneView.RepaintAll();
					Event.current.Use();
					return;
				}
            }

            //Single mouse click
            if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                isSelecting = true;
                startMousePos = Event.current.mousePosition;

                int triangleHit = RayPick();
                if (triangleHit >= 0)
                {
                    _Source.selectedTriangles[triangleHit] = !_Source.selectedTriangles[triangleHit];
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
                                    if (Vector3.Dot(normal, SceneView.currentDrawingSceneView.camera.transform.forward) < -0.0f)
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
                                if (Vector3.Dot(centerNormal, SceneView.currentDrawingSceneView.camera.transform.forward) < -0.0f)
                                    found = true;
                            }
                            else
                                found = true;
                        }

                        if (found)
                            _Source.selectedTriangles[(i / 3)] = setSelectedOn;
                    }

                    _Source.UpdateSelectionMesh();
                }
            }

            if (Event.current.type == EventType.MouseMove)
            {
                SceneView.RepaintAll();
            }
        }

        private int RayPick()
        {
            if (Camera.current == null)
            {
                Debug.LogWarning("Camera is null!");
                return -1;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit))
                return -1;

            MeshCollider meshCollider = hit.collider as MeshCollider;
            if (meshCollider == null || meshCollider.sharedMesh == null || meshCollider != _Source.meshCollider)
                return -1;

            return hit.triangleIndex;
        }

		/// <summary>
		/// Checks if the specified ray hits the triangle descibed by p1, p2 and p3.
		/// Möller–Trumbore ray-triangle intersection algorithm implementation.
		/// Source from Unity Answers
		/// </summary>
		/// <param name="ray">The ray to test hit for.</param>
		/// <param name="p1">Vertex 1 of the triangle.</param>
		/// <param name="p2">Vertex 2 of the triangle.</param>
		/// <param name="p3">Vertex 3 of the triangle.</param>
		/// <returns><c>true</c> when the ray hits the triangle, otherwise <c>false</c></returns>
		public static bool RayTriIntersect(Ray ray, Vector3 p1, Vector3 p2, Vector3 p3)
		{
			// Vectors from p1 to p2/p3 (edges)
			Vector3 e1, e2;  

			Vector3 p, q, t;
			float det, invDet, u, v;


			//Find vectors for edges sharing vertex/point p1
			e1 = p2 - p1;
			e2 = p3 - p1;

			// Calculate determinant 
			p = Vector3.Cross(ray.direction, e2);

			//Calculate determinat
			det = Vector3.Dot(e1, p);

			//if determinant is near zero, ray lies in plane of triangle otherwise not
			if (det > -Mathf.Epsilon && det < Mathf.Epsilon) { return false; }
			invDet = 1.0f / det;

			//calculate distance from p1 to ray origin
			t = ray.origin - p1;

			//Calculate u parameter
			u = Vector3.Dot(t, p) * invDet;

			//Check for ray hit
			if (u < 0 || u > 1) { return false; }

			//Prepare to test v parameter
			q = Vector3.Cross(t, e1);

			//Calculate v parameter
			v = Vector3.Dot(ray.direction, q) * invDet;

			//Check for ray hit
			if (v < 0 || u + v > 1) { return false; }

			if ((Vector3.Dot(e2, q) * invDet) > Mathf.Epsilon)
			{ 
				//ray does intersect
				return true;
			}

			// No hit at all
			return false;
		}

		private void RaycastHide()
		{
			if (_Source == null)
				return;

			Mesh targetMesh = _Source.sharedMesh;
			if (targetMesh == null)
				return;
			
			Mesh occlusionMesh = _Source.occlusionMesh;
			if (occlusionMesh == null)
				return;
			
			Vector3[] targetVerts = targetMesh.vertices;
			Vector3[] targetNorms = targetMesh.normals;
			if (targetNorms.Length != targetVerts.Length)
				return;
			
			Vector3[] occlusionVerts = occlusionMesh.vertices;
			List<int[]> occlusionTriangles = new List<int[]>();
			for (int i = 0; i < occlusionMesh.subMeshCount; i++)
			{
				occlusionTriangles.Add(occlusionMesh.GetTriangles(i));
			}

			BitArray vertexOccluded = new BitArray(targetVerts.Length);
			for (int i = 0; i < targetVerts.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Progress", "calculating...", ((float)i / (float)targetVerts.Length));
                    
				Ray testRay = new Ray(targetVerts[i], targetNorms[i]);
				for (int j = 0; j < occlusionTriangles.Count; j++)
				{
					int[] triVerts = occlusionTriangles[j];
					for (int k = 0; k < triVerts.Length; k+= 3)
					{
						if (RayTriIntersect(testRay,
							occlusionVerts[triVerts[k + 0]],
							occlusionVerts[triVerts[k + 1]],
							occlusionVerts[triVerts[k + 2]]))
						{
							vertexOccluded[i] = true;
							break;
						}
					}

					if (vertexOccluded[i])
						continue;
				}
			}
            EditorUtility.ClearProgressBar();

			_Source.selectedTriangles.SetAll(false);

			for (int i = 0; i < /*_Source.sharedMesh.subMeshCount*/ 1; i++)
			{
				int[] triVerts = _Source.sharedMesh.GetTriangles(i);
				for (int j = 0; j < triVerts.Length; j += 3)
				{
					if (vertexOccluded[triVerts[j + 0]] &&
						vertexOccluded[triVerts[j + 1]] &&
						vertexOccluded[triVerts[j + 2]])
					{
                        _Source.selectedTriangles[(j / 3)] = true;
					}
				}
			}

			_Source.UpdateSelectionMesh();
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
            }
        }
    }
}
