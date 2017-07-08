﻿using System.Collections;
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
        private bool showWireframe = true;

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

            bool toggled = GUILayout.Toggle(showWireframe, "Show Wireframe", "Button");
            if (toggled != showWireframe) { UpdateShadingMode(toggled); }           
            showWireframe = toggled;

            if (GUILayout.Button("Clear All"))
            {
                source.ClearAll();
            }

            if (GUILayout.Button("Select All"))
            {
                source.SelectAll();
            }

            if (GUILayout.Button("Done Editing"))
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
                    source.selectedTriangles[triangleHit[0]] = !source.selectedTriangles[triangleHit[0]];
                    source.selectedTriangles[triangleHit[1]] = !source.selectedTriangles[triangleHit[1]];
                    source.selectedTriangles[triangleHit[2]] = !source.selectedTriangles[triangleHit[2]];
                        
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