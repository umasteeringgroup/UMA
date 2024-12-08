using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class VertexEditorStage : PreviewSceneStage
{
    public PreviewWindow ownerWindow;
    public GUIContent titleContent;
    public SceneView openedSceneView;
    public GameObject selectedObject;
    public GameObject VertexObject;
    GameObject lightingObject = null;
    public bool NeedsCameraSetup = false;
    public DynamicCharacterAvatar thisDCA;
    public Mesh BakedMesh;
    public int selectedVertex;
    private List<VertexSelection> SelectedVertexes = new List<VertexSelection>();
    PhysicsScene phyScene;
    float HandlesSize = 0.01f;

    const int VertexCollectionsWindowID = 0x1234;
    const int VisibleSlotsWindowID = 0x1235;
    const int SlotAdjustmentsWindowID = 0x1236;
    const int CurrentSlotAdjustmentsWindowID = 0x1237;
    const int TestWindowID = 0x1238;

    public Vector2 scrollLocation = Vector2.zero;
    public Rect VertexCollectionWindow = new Rect(10, 10, 200, 300);
    public Rect VisibleSlotsWindow = new Rect(10, 310, 200, 300);
    public Rect SlotAdjustmentsWindow = new Rect(210, 10, 200, 300);
    public Rect CurrentSlotAdjustmentsWindow = new Rect(210, 310, 200, 300);
    public Rect TestWindow = new Rect(200, 200, 200, 200);

    private class VertexSelection
    {
        public int vertexIndexOnSlot;
        public SlotData slot;
        public Vector3 WorldPosition;
    }

    public static void ShowStage(DynamicCharacterAvatar DCA)
    {
        VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
        stage.titleContent = new GUIContent();
        stage.titleContent.text = Selection.activeGameObject.name;
        stage.titleContent.image = EditorGUIUtility.IconContent("GameObject Icon").image;
        stage.thisDCA = DCA;
        StageUtility.GoToStage(stage, true);
    }


    protected override bool OnOpenStage()
    {
        base.OnOpenStage();
        //scene = EditorSceneManager.NewPreviewScene();

        GameObject lightingObject = new GameObject("Directional Light");
        lightingObject.transform.rotation = Quaternion.Euler(50, 330, 0);
        lightingObject.AddComponent<Light>().type = LightType.Directional;

        SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();

        BakedMesh = new Mesh();
        BakedMesh.name = "BakedMesh";
        smr.BakeMesh(BakedMesh, true);
        GameObject go = new GameObject("VertexEditor");
        go.AddComponent<MeshFilter>().sharedMesh = BakedMesh;
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = go.AddComponent<MeshRenderer>();
        }
        // Material sharedMaterial = UMAUtils.GetDefaultDiffuseMaterial();
        renderer.sharedMaterials = new Material[BakedMesh.subMeshCount];
        //go.transform.parent = thisDCA.gameObject.transform;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = BakedMesh;

        go.SetActive(true);
        smr.enabled = false;
        VertexObject = go;
        SetVertexMaterialColors(go);

        SceneManager.MoveGameObjectToScene(VertexObject, scene);
        SceneManager.MoveGameObjectToScene(lightingObject, scene);
        
        Tools.hidden = true;
        SceneView.duringSceneGui += OnSceneGUI;
        NeedsCameraSetup = true;
        return true;
    }


    protected override void OnCloseStage()
    {
        Tools.hidden = false;
        DestroyImmediate(VertexObject);
        DestroyImmediate(lightingObject);
        SceneView.duringSceneGui -= OnSceneGUI;
        base.OnCloseStage();
    }

    private void OnSceneGUI(SceneView view)
    {
        if (NeedsCameraSetup)
        {
            InitialSetup(view);
        }
        DoSceneGUI(view);
/*
        Handles.BeginGUI();
        if (GUI.Button(new Rect(40,40,200,20), "Hello from scene "+scene.handle))
        {
            Selection.activeObject = VertexObject;
            view.FrameSelected(false, true);
        }
        Handles.EndGUI(); */
    }



    private void DoSceneGUI(SceneView sceneView)
    {
        //   if (!AllowVertexSelection)
        //   {
        //       return;
        //   }

        Handles.SetCamera(sceneView.camera);

        Event currentEvent = Event.current;

        string vals = $"Shift {currentEvent.shift}\nControl{currentEvent.control}\n,Alt{currentEvent.alt},Command{currentEvent.command}";


        if (currentEvent.type == EventType.Repaint)
        {
            if (currentEvent.shift)
            {
                EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.ArrowPlus);
            } 
            else if (currentEvent.control)
            {
                EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.ArrowMinus);
            }
            else
            {
                EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.Arrow);
            }
        }

        if (currentEvent.type == EventType.MouseDown)
        {
            if (currentEvent.button == 0)
            {
                //Debug.Log("Left mouse button pressed in Scene View");
                Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
                if (phyScene.Raycast(ray.origin,ray.direction, out RaycastHit hit))
                {
                   // Debug.Log("Hit something: " + hit.transform.gameObject.name);
                    bool duplicateVertex = false;
                    if (hit.transform != null && hit.transform.gameObject == VertexObject)
                    {
                        VertexSelection vs = FindVertex(hit, BakedMesh, VertexObject);

                        for (int i = 0; i < SelectedVertexes.Count; i++)
                        {
                            if (SelectedVertexes[i].slot.slotName == vs.slot.slotName && SelectedVertexes[i].vertexIndexOnSlot == vs.vertexIndexOnSlot)
                            {
                                selectedVertex = i;
                                duplicateVertex = true;
                                break;
                            }
                        }

                        if (!duplicateVertex)
                        {
                            if (currentEvent.shift)
                            {
                                SelectedVertexes.Add(vs);
                                selectedVertex = SelectedVertexes.Count - 1;
                            }

                            //Repaint();// repaint the inspector to show the new vertex.
                        }
                        else
                        {
                            if (currentEvent.control)
                            {
                                SelectedVertexes.RemoveAt(selectedVertex);
                                selectedVertex = -1;
                            }
                        }
                    }
                }
            }
            else if (currentEvent.button == 1)
            {
            }
        }

        Color saveColor = Handles.color;

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
            // Debug.Log("Drawing vertex " + i + " at " + vs.WorldPosition);
            Handles.SphereHandleCap(i, vs.WorldPosition, Quaternion.identity, HandlesSize, EventType.Repaint);
        }

        Handles.color = saveColor;

        // Your custom GUI logic here
        Handles.BeginGUI();
        VertexCollectionWindow = GUI.Window(VertexCollectionsWindowID, VertexCollectionWindow, (id) =>
        {
            GUILayout.Label("Vertex Collections");
            foreach (var vs in SelectedVertexes)
            {
                GUILayout.Label(vs.slot.slotName + " " + vs.vertexIndexOnSlot);
            }
            GUI.DragWindow();
        }, "Vertex Collections");


        TestWindow = GUI.Window(TestWindowID, TestWindow, (id) =>
        {
            if (GUILayout.Button("Test Button"))
            {
                SelectedVertexes.Clear();
            }
            scrollLocation = EditorGUILayout.BeginScrollView(scrollLocation);
            foreach (var vs in SelectedVertexes)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(vs.slot.slotName + " " + vs.vertexIndexOnSlot);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            GUI.DragWindow();
        }, "Test Windowz");



        VisibleSlotsWindow = GUI.Window(VisibleSlotsWindowID, VisibleSlotsWindow, (id) =>
        {
            bool wasChanged = false;
            //GUILayout.Label("Visible Slots");
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                GUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                slot.isDisabled = GUILayout.Toggle(slot.isDisabled, "");
                wasChanged = EditorGUI.EndChangeCheck();
                GUILayout.Label(slot.slotName);
                GUILayout.EndHorizontal();

            }
            GUI.DragWindow();
            if (wasChanged)
            {
                RebuildMesh();
            }
        }, "Visible Slots");


        SlotAdjustmentsWindow = GUI.Window(SlotAdjustmentsWindowID, SlotAdjustmentsWindow, (id) =>
        {
            GUILayout.Label("Slot Adjustments");
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                GUILayout.Label(slot.slotName);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Edit"))
                {
                    // Show the slot adjustments window
                }
                GUILayout.EndHorizontal();
            }
            GUI.DragWindow();
        }, "Slot Adjustments");

        CurrentSlotAdjustmentsWindow = GUI.Window(CurrentSlotAdjustmentsWindowID, CurrentSlotAdjustmentsWindow, (id) =>
        {
            GUILayout.Label("Current Slot Adjustments");
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (slot.isDisabled)
                {
                    continue;
                }
                GUILayout.Label(slot.slotName);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Edit"))
                {
                    // Show the slot adjustments window
                }
                GUILayout.EndHorizontal();
            }
            GUI.DragWindow();
        }, "Current Slot Adjustments");

        Handles.EndGUI();

        /*
        GUILayout.BeginArea(new Rect(10, 10, 300, 300), "Vertex Selection", GUI.skin.window);
        EditorGUILayout.HelpBox("Shift-Click to add\nCtrl-Click to remove\nClick to select", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Handle Sz", GUILayout.Width(96));
        HandlesSize = EditorGUILayout.Slider(HandlesSize, 0.0f, 0.04f,GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            SceneView.RepaintAll();
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Vertex Selection"))
        {
            SelectedVertexes.Clear();
            selectedVertex = -1;
        }
        if (GUILayout.Button("Exit Vertex Selection"))
        {
            StageUtility.GoBackToPreviousStage();
            SceneView.RepaintAll();
        }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        */


        // Repaint the scene view only when necessary
        if (currentEvent.type == EventType.Repaint)
        {
            SceneView.RepaintAll();
        }
    }

    private void RebuildMesh()
    {
        thisDCA.umaData.Dirty(false, false, true);
        thisDCA.umaData.CharacterUpdated.AddAction(BuildCollisionMesh);
    }

    public void BuildCollisionMesh(UMAData umaData)
    {
        thisDCA.umaData.CharacterUpdated.RemoveAction(BuildCollisionMesh);
        SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
        GameObject.DestroyImmediate(BakedMesh);
        BakedMesh = new Mesh();
        BakedMesh.name = "BakedMesh";
        smr.BakeMesh(BakedMesh, true);
        VertexObject.GetComponent<MeshFilter>().sharedMesh = BakedMesh;
        MeshCollider mc = VertexObject.GetComponent<MeshCollider>();
        mc.sharedMesh = BakedMesh;
    }


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



    protected override GUIContent CreateHeaderContent()
    {
        GUIContent headerContent = new GUIContent();
        headerContent.text = "UMA Vertex Editing";
        headerContent.image = titleContent.image;
        return headerContent;
    }

    protected void InitialSetup(SceneView sceneView)
    {
        // Frame in scene view
        // This doesn't work in 2021.3
        Selection.activeObject = VertexObject;
        sceneView.FrameSelected(false, true);
        NeedsCameraSetup = false;

        Tools.current = Tool.None;
        Tools.hidden = true;

        // Setup Scene view state
        sceneView.sceneViewState.showFlares = false;
        sceneView.sceneViewState.alwaysRefresh = false;
        sceneView.sceneViewState.showFog = false;
        sceneView.sceneViewState.showSkybox = false;
        sceneView.sceneViewState.showImageEffects = false;
        sceneView.sceneViewState.showParticleSystems = false;
        sceneView.sceneLighting = false;

        // this doesn't work in 2021.3
        Tools.hidden = true;

        sceneView.camera.transform.position = new Vector3(0, 1, -5);
        Selection.activeObject = VertexObject;
        sceneView.FrameSelected(false, true);
        phyScene = PhysicsSceneExtensions.GetPhysicsScene(scene);
    }



    private Color[] defaultColors = new Color[]
{
            new Color(1.0f, 0.9f, 0.9f, 1.0f),
            new Color(0.9f, 1.0f, 0.9f, 1.0f),
            new Color(0.9f, 0.9f, 1.0f, 1.0f),
            new Color(1.0f, 1.0f, 0.9f, 1.0f),
            new Color(0.9f, 1.0f, 1.0f, 1.0f),
            new Color(1.0f, 0.9f, 1.0f, 1.0f)
};


    private void SetVertexMaterialColors(GameObject VertexObject)
    {
        MeshRenderer mr = VertexObject.GetComponent<MeshRenderer>();
        List<Material> newMaterials = new List<Material>();

        if (mr != null)
        {
            for (int i = 0; i < mr.sharedMaterials.Length; i++)
            {
                int colorNo = i % defaultColors.Length;
                if (mr.sharedMaterials[i] == null)
                {
                    Material M = UMAUtils.GetDefaultDiffuseMaterial();
                    if (M != null)
                    {
                        M.SetColor("_Color", defaultColors[colorNo]);
                        newMaterials.Add(M);
                    }
                    else
                    {
                        Debug.LogError("No Default Material found");
                    }
                }
            }
            mr.sharedMaterials = newMaterials.ToArray();
        }
        else
        {
            Debug.LogError("No MeshRenderer found");
        }
    }
}
