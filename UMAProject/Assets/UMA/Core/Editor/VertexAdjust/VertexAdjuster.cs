using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA
{

    public class VertexAdjuster : IEditorScene
    {
        InteractiveUMAWindow sceneView;
        public DynamicCharacterAvatar thisDCA;
        const string vertexSelectionToolName = "VertexSelection";
        public int selectedVertex = -1;
        public GameObject VertexObject;
        public Mesh BakedMesh;

        private class VertexSelection
        {
            public int vertexIndexOnSlot;
            public SlotData slot;
            public Vector3 WorldPosition;
        }

        private List<VertexSelection> SelectedVertexes = new List<VertexSelection>();


        public void Setup(DynamicCharacterAvatar dca)
        {
            Debug.Log("Vertex Adjuster Setup");
            thisDCA = dca;
            GameObject go = GameObject.Find(vertexSelectionToolName);
            if (go == null)
            {
                go = new GameObject(vertexSelectionToolName);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.hideFlags = HideFlags.HideAndDontSave;
                VertexObject = go;
            }

            SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                BakedMesh = new Mesh();
                smr.BakeMesh(BakedMesh);
            }


        }

        #region IEditorScene
        public void Cleanup(InteractiveUMAWindow scene)
        {
            GameObject go = GameObject.Find(vertexSelectionToolName);
            if (go != null)
            {
                GameObject.DestroyImmediate(go);
                BakedMesh = null;
            }
            if (BakedMesh != null)
            {
                GameObject.DestroyImmediate(BakedMesh);
            }
        }

        public void InitializationComplete(GameObject root)
        {
            //throw new System.NotImplementedException();
        }

        public void Initialize(InteractiveUMAWindow sceneView, Scene scene)
        {
            Debug.Log("VertexAdjuster Initialize");
            this.sceneView = sceneView;
            SceneManager.MoveGameObjectToScene(VertexObject, scene);
        }

        public void OnSceneGUI(InteractiveUMAWindow scene)
        {
            Event currentEvent = Event.current;
            if (sceneView != null)
            {
                EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.ArrowPlus);
            }
            if (currentEvent.type == EventType.MouseDown)
            {
                if (currentEvent.button == 0 && currentEvent.shift)
                {
                    Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        bool duplicateVertex = false;
                        if (hit.transform != null && hit.transform.gameObject.name == vertexSelectionToolName)
                        {
                            //Debug.Log("Hit the vertex editor mesh: " + hit.point);
                            VertexSelection vs = FindVertex(hit, BakedMesh, VertexObject);

                            for (int i = 0; i < SelectedVertexes.Count; i++)
                            {
                                if (SelectedVertexes[i].slot.slotName == vs.slot.slotName && SelectedVertexes[i].vertexIndexOnSlot == vs.vertexIndexOnSlot)
                                {
                                    selectedVertex = i;
                                    duplicateVertex = true;
                                    //Repaint();
                                    break;
                                }
                            }

                            if (!duplicateVertex)
                            {
                                // todo: get actual vertex index and slot.
                                SelectedVertexes.Add(vs);// new VertexSelection() { vertexIndexOnSlot = 0, slotName = "SlotName", WorldPosition = hit.point });
                                selectedVertex = SelectedVertexes.Count - 1;
                                //Repaint();// repaint the inspector to show the new vertex.
                            }
                        }
                    }
                }
                else if (currentEvent.button == 1)
                {
                    Debug.Log("Right mouse button pressed in Scene View");
                }
            }

            Color saveColor = Handles.color;

            float size = 0.003f;
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                VertexSelection vs = SelectedVertexes[i];
                if (i == selectedVertex)
                {
                    Handles.color = Color.yellow;
                }
                else
                {
                    Handles.color = Color.red;
                }
                Handles.SphereHandleCap(i, vs.WorldPosition, Quaternion.identity, size, EventType.Repaint);
            }

            Handles.color = saveColor;

            // Your custom GUI logic here
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(10, 10, 200, 300), "Vertex Selection", GUI.skin.window);

            EditorGUILayout.HelpBox("Hold shift and click on the Avatar to select a vertex.", MessageType.Info);
            if (GUILayout.Button("Disables Vertex Selection"))
            {
                //AllowVertexSelection = false;
                //CleanupFromVertexMode();
                SceneView.RepaintAll();
            }
            GUILayout.EndArea();
            Handles.EndGUI();

            // Repaint the scene view only when necessary
            if (currentEvent.type == EventType.Repaint)
            {
                SceneView.RepaintAll();
            }
        }

        public void ShowHelp(bool isShown)
        {

        }
        #endregion

        private VertexSelection FindVertex(RaycastHit hit, Mesh mesh, GameObject go)
        {
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            int triangle = hit.triangleIndex;

            var tris = mesh.triangles;
            var verts = mesh.vertices;

            int i0 = tris[triangle * 3];
            Vector3 local = go.transform.InverseTransformPoint(hit.point);

            int foundVert = tris[triangle * 3];
            float maxDist = Vector3.Distance(local, verts[foundVert]);

            for (int i = 0; i < 3; i++)
            {
                Vector3 vert = verts[tris[triangle * 3 + i]];
                float dist = Vector3.Distance(local, vert);
                if (dist < maxDist)
                {
                    maxDist = dist;
                    foundVert = tris[triangle * 3 + i];
                }
            }

            SlotData foundSlot = thisDCA.umaData.umaRecipe.FindSlotForVertex(foundVert);

            if (foundSlot != null)
            {
                int LocalToSlot = foundVert - foundSlot.vertexOffset;
                return new VertexSelection()
                {
                    vertexIndexOnSlot = LocalToSlot,
                    slot = foundSlot,
                    WorldPosition = go.transform.TransformPoint(verts[foundVert])
                };
            }
            throw new Exception("Vertex not found on slots!");
        }
    }
}
