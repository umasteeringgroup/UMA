using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    [CustomEditor(typeof(GeometrySelector))]
    public class GeometrySelectorInspector : Editor
    {
        //public bool showWireFrame = true;
        private bool doneEditing = false;
        private SerializedProperty showWireframe;

        void OnEnable()
        {
            EditorApplication.update += GeometryUpdate;
            showWireframe = serializedObject.FindProperty("showWireframe");
        }

        public override void OnInspectorGUI()
        {
            GeometrySelector source = target as GeometrySelector;

            //base.OnInspectorGUI();
            serializedObject.Update();

            EditorGUILayout.PropertyField(showWireframe);
            //showWireFrame = EditorGUILayout.Toggle("ShowWireFrame",showWireFrame);

            var obj = EditorGUILayout.ObjectField("SharedMesh", source.sharedMesh, typeof(Mesh), false);
            if (obj != null && obj != source.sharedMesh)
            {
                source.sharedMesh = obj as Mesh;
                EditorUtility.SetDirty(target);
            }

            if (GUILayout.Button("Done with Edits"))
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

            if (Event.current.type == EventType.layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode(), FocusType.Passive));
            
            if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                int[] triangleHit = RayPick();
                if (triangleHit != null)
                {
                    if (source.selectedTriangles.Contains(triangleHit[0]))
                    {
                        source.selectedTriangles.Remove(triangleHit[0]);

                        //Change this in the future so that instead of trying to keep the two in sync, just update the asset when done editing
                        if (source.meshAsset != null)
                        {
                            source.meshAsset.SetTriangleFlag(triangleHit[0], false);
                        }
                    }
                    else
                    {
                        source.selectedTriangles.Add(triangleHit[0]);//let's only store the first index of the triangle hit

                        //Change this in the future so that instead of trying to keep the two in sync, just update the asset when done editing
                        if (source.meshAsset != null)
                        {
                            source.meshAsset.SetTriangleFlag(triangleHit[0], true);;
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
            if (meshCollider == null || meshCollider.sharedMesh == null)
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
            GeometrySelector editObject = target as GeometrySelector;
            if(editObject.meshAsset != null) 
                Selection.activeObject = editObject.meshAsset;
            DestroyImmediate(editObject.gameObject);
        }
    }
}
