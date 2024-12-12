using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class VertexEditorStage : PreviewSceneStage
{
    public PreviewWindow ownerWindow;
    public GUIContent titleContent;
    public SceneView openedSceneView;
    public GameObject selectedObject;
    public GameObject VertexObject;
    public GameObject cameraAnchor;
    GameObject lightingObject = null;
    public bool NeedsCameraSetup = false;
    public bool closing = false;
    public DynamicCharacterAvatar thisDCA;
    public Mesh BakedMesh;
    public int selectedVertex;
    private List<VertexSelection> SelectedVertexes = new List<VertexSelection>();
    PhysicsScene phyScene;
    float HandlesSize = 0.01f;

    const int VertexEditorToolsWindowID = 0x1234;
    const int VisibleWearablesID = 0x1235;
    const int SlotAdjustmentsWindowID = 0x1236;
    const int CurrentSlotAdjustmentsWindowID = 0x1237;
    const int TestWindowID = 0x1238;

    public Vector2 VertexEditorScrollLocation = Vector2.zero;
    public Vector2 VisibleWearablesLocation = Vector2.zero;
    public Rect VertexEditorToolsWindow = new Rect(10, 10, 200, 300);
    public Rect VisibleWearablesWindow = new Rect(10, 310, 200, 300);
    public Rect SlotAdjustmentsWindow = new Rect(210, 10, 200, 300);
    public Rect CurrentSlotAdjustmentsWindow = new Rect(210, 310, 200, 300);
    private MeshModifierEditor modifierEditor;
    public bool rectSelect = false;
    public Vector2 RectStart = Vector2.zero;

    private class VertexSelection
    {
        public int vertexIndexOnSlot;
        public SlotData slot;
        public Vector3 WorldPosition;
    }

    public static VertexEditorStage ShowStage(DynamicCharacterAvatar DCA)
    {
        VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
        stage.titleContent = new GUIContent();
        stage.titleContent.text = Selection.activeGameObject.name;
        stage.titleContent.image = EditorGUIUtility.IconContent("GameObject Icon").image;
        stage.thisDCA = DCA;
        StageUtility.GoToStage(stage, true); 
        return stage;
    }


    protected override bool OnOpenStage()
    {
        base.OnOpenStage();
        //scene = EditorSceneManager.NewPreviewScene();

        modifierEditor = MeshModifierEditor.GetOrCreateWindow(thisDCA, this);
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
        cameraAnchor = new GameObject("CameraAnchor");
        cameraAnchor.transform.position = new Vector3(0, 1, 2.5f);
        cameraAnchor.transform.rotation = Quaternion.Euler(0, 180, 0);

        SceneManager.MoveGameObjectToScene(VertexObject, scene);
        SceneManager.MoveGameObjectToScene(lightingObject, scene);
        SceneManager.MoveGameObjectToScene(cameraAnchor, scene);       
        Tools.hidden = true;
        SceneView.duringSceneGui += OnSceneGUI;
        NeedsCameraSetup = true;
        return true;
    }


    protected override void OnCloseStage()
    {
        closing = true;
        Tools.hidden = false;
        DestroyImmediate(VertexObject);
        DestroyImmediate(lightingObject);
        DestroyImmediate(cameraAnchor);
        SceneView.duringSceneGui -= OnSceneGUI;
        var wearables = thisDCA.GetVisibleWearables();
        foreach (var wearable in wearables)
        {
            wearable.disabled = false;
        }
        if (thisDCA.editorTimeGeneration)
        {
            thisDCA.GenerateSingleUMA();
        }
        if (modifierEditor != null)
        {
            modifierEditor.Close();
        }
        if (vertexMaterial != null)
        {
            DestroyImmediate(vertexMaterial);
        }
        if (vertexMesh != null)
        {
            DestroyImmediate(vertexMesh);
        }
        base.OnCloseStage();
    }

    private void OnSceneGUI(SceneView view)
    {
        if (NeedsCameraSetup)
        {
            InitialSetup(view);
        }
        AdjustWindowRects();
        DoSceneGUI(view);
    }

    public void AdjustWindowRects()
    {
        // Adjust window positions based on the scene view size
        Rect r = SceneView.lastActiveSceneView.position;
        float width = 200;
        float halfheight = (r.height / 2)-45;
        float top1 = 5;
        float top2 = halfheight + 10;
        float left1 = 5;
        float left2 = r.width - width - 35;

        VertexEditorToolsWindow = new Rect(left1, top1, width, halfheight);
        VisibleWearablesWindow = new Rect(left1, top2, width, halfheight);
        SlotAdjustmentsWindow = new Rect(left2, top1, width, halfheight);
        CurrentSlotAdjustmentsWindow = new Rect(left2, top2, width, halfheight);
    }


    private void DoSceneGUI(SceneView sceneView)
    {
        Handles.SetCamera(sceneView.camera);
        if (!rectSelect && Event.current.alt)
        {
            DrawHandles();
            return;
        }


        Event currentEvent = Event.current;

        string vals = $"Shift {currentEvent.shift}\nControl{currentEvent.control}\n,Alt{currentEvent.alt},Command{currentEvent.command}";

        if (Event.current.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode(), FocusType.Passive));
        }

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
                if (currentEvent.shift)
                {
                    SingleSelect(currentEvent);
                    rectSelect = false;
                }
                else if (currentEvent.control)
                {
                    SingleSelect(currentEvent);
                    rectSelect = false;
                }
                else
                {
                    rectSelect = true;
                    RectStart = currentEvent.mousePosition - currentEvent.delta;
                }
            }
            else if (currentEvent.button == 1)
            {
                rectSelect = false;
            }
        }

        // This is to prevent the scene view from capturing the selection and doing it's own routines
        if (currentEvent.type == EventType.MouseDrag)
        {
            Event.current.Use();
            sceneView.Repaint();
        }

        if (currentEvent.type == EventType.MouseUp)// && currentEvent.button == 0)
        {
            if (rectSelect)
            {
                // Do the rectangle selection
                Vector2 RectEnd = currentEvent.mousePosition;
                Rect MinMax = GetMinMax(RectStart, RectEnd);
                RectangleSelect(currentEvent, MinMax);
                rectSelect = false;
            }
        }



        if (currentEvent.type == EventType.MouseLeaveWindow)
        {
            if (rectSelect)
            {
                Vector2 RectEnd = currentEvent.mousePosition;
                Rect MinMax = GetMinMax(RectStart, RectEnd);
                RectangleSelect(currentEvent, MinMax);
                rectSelect = false;
                sceneView.Repaint();
            }
        }

        if (rectSelect && (currentEvent.mousePosition.x < 0 || currentEvent.mousePosition.y < 0 || currentEvent.mousePosition.x > sceneView.position.width || currentEvent.mousePosition.y > sceneView.position.height))
        {
            rectSelect = false;
        }
        if (rectSelect)
        {
            Handles.BeginGUI();
            GUI.Box(new Rect(RectStart.x, RectStart.y, currentEvent.mousePosition.x - RectStart.x, currentEvent.mousePosition.y - RectStart.y), "");
            Handles.EndGUI();
        }

        DrawHandles();

        // Your custom GUI logic here
        DrawGUIWindows(sceneView);

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

    private void DrawHandles()
    {
        Color LastColor = Color.black;
        if (EventType.Repaint != Event.current.type)
        {
            return;
        }
        Color saveColor = Handles.color;
        Mesh mesh = GetVertexMesh();
        Material mat = GetVertexMaterial(Color.red);


        for (int i = 0; i < SelectedVertexes.Count; i++)
        {
            VertexSelection vs = SelectedVertexes[i];
            int bakedIndex = vs.vertexIndexOnSlot + vs.slot.vertexOffset;
            Vector3 bakedNormal = BakedMesh.normals[bakedIndex];
            if (Vector3.Dot(bakedNormal, Camera.current.transform.forward) > 0)
            {
                continue;
            }
#if USING_HANDLES
            if (i == selectedVertex)
            {
                Handles.color = Color.yellow;
            }
            else
            {
                Handles.color = Color.red;
            }
//            Handles.CubeHandleCap(i, vs.WorldPosition, Quaternion.identity, HandlesSize, EventType.Repaint);
            Handles.SphereHandleCap(i, vs.WorldPosition, Quaternion.identity, HandlesSize, EventType.Repaint);
        Handles.color = saveColor;

#else
            Matrix4x4 matrix = Matrix4x4.TRS(vs.WorldPosition, Quaternion.identity, Vector3.one * HandlesSize);

            Color newColor = Color.red;
          
            if (i == selectedVertex)
            {
                newColor = Color.yellow;
            }


            if (newColor != LastColor)
            {
                LastColor = newColor;
                mat.SetColor("_Color", newColor);
                mat.SetPass(0);
            }
            Graphics.DrawMeshNow(mesh, matrix);
            //Graphics.DrawMesh(mesh, matrix, mat, 0);
#endif
        }




        //DrawGUI();
    }

    private void DrawGUIWindows(SceneView sceneView)
    {
        Handles.BeginGUI();

        VertexEditorToolsWindow = GUI.Window(VertexEditorToolsWindowID, VertexEditorToolsWindow, (id) =>
        {
            GUILayout.Label("Vertex Collections");
            GUILayout.Label("camera: " + sceneView.camera.transform.position.ToString());
            if (GUILayout.Button("Home Camera"))
            {
                SceneView.lastActiveSceneView.pivot = new Vector3(0, 1, 2.5f);
                Selection.activeObject = VertexObject;
                sceneView.AlignViewToObject(cameraAnchor.transform);
                sceneView.FrameSelected();
                sceneView.AlignViewToObject(cameraAnchor.transform);
            }
        }, "Vertex Collections");



        VisibleWearablesWindow = GUI.Window(VisibleWearablesID, VisibleWearablesWindow, (id) =>
        {
            VisibleWearablesLocation = GUILayout.BeginScrollView(VisibleWearablesLocation);
            bool wasChanged = false;
            bool wasRecipeChanged = false;
            var wearables = thisDCA.GetVisibleWearables();

            foreach (var wearable in wearables)
            {
                GUILayout.BeginHorizontal();
                bool wasVisible = wearable.disabled;
                wearable.disabled = !GUILayout.Toggle(!wearable.disabled, "");
                if (wasVisible != wearable.disabled)
                {
                    wasRecipeChanged = true;
                }
                GUILayout.Label(wearable.name);
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("Visible Slots");
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                GUILayout.BeginHorizontal();
                bool wasDisabled = slot.Suppressed;
                // EditorGUI.BeginChangeCheck();  doesn't work here
                slot.Suppressed = !GUILayout.Toggle(!slot.Suppressed, "");
                //wasChanged = EditorGUI.EndChangeCheck();
                if (slot.Suppressed != wasDisabled)
                {
                    wasChanged = true;
                }
                GUILayout.Label(slot.slotName);
                GUILayout.EndHorizontal();

            }
            GUILayout.EndScrollView();
            if (wasChanged)
            {
                Debug.Log("Rebuilding mesh");
                RebuildMesh(false);
            }
            if (wasRecipeChanged)
            {
                Debug.Log("Recipe changed");
                RebuildMesh(true);
            }
        }, "Visible Wearables");


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
                if (slot.Suppressed)
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
    }

    private Rect GetMinMax(Vector2 rectStart, Vector2 rectEnd)
    {
        float xMin = Mathf.Min(rectStart.x, rectEnd.x);
        float xMax = Mathf.Max(rectStart.x, rectEnd.x);
        float yMin = Mathf.Min(rectStart.y, rectEnd.y);
        float yMax = Mathf.Max(rectStart.y, rectEnd.y);

        Rect MinMax = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);

        Debug.Log("RectStart: " + rectStart.ToString() + " RectEnd: " + rectEnd.ToString());
        Debug.Log("MinMax: " + MinMax.ToString());
        Debug.Log($"xMin: {xMin}, xMax: {xMax}, yMin: {yMin}, yMax: {yMax}");
        return MinMax;
    }

    private void RectangleSelect(Event currentEvent, Rect ScreenArea)
    {
        Debug.Log("Rectangle Select - Area: " + ScreenArea.ToString());
        var vertexes = BakedMesh.vertices;
        for (int i = 0; i < vertexes.Length; i++)
        {
            Vector3 screenPos = HandleUtility.WorldToGUIPoint(VertexObject.transform.TransformPoint(vertexes[i]));
            if (ScreenArea.Contains(screenPos))
            {
                bool blocked = false;

                Vector3 Normal = BakedMesh.normals[i];
                // if the normal is not facing the camera
                if (Vector3.Dot(Normal, Camera.current.transform.forward) > 0)
                {
                    continue;
                }

                // do a raycast here from the camera to the vertex (expanded by the normal * 1.001)
                Ray ray = HandleUtility.GUIPointToWorldRay(screenPos);
                if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
                {
                    if (hit.transform != null && hit.transform.gameObject == VertexObject)
                    {
                        float dist = Vector3.Distance(VertexObject.transform.TransformPoint(vertexes[i]), hit.point);
                        if (dist > 0.001f)
                        {
                            blocked = true;
                        }
                    }
                }




                if (!blocked)
                {
                    bool duplicateVertex = false;
                    SlotData foundSlot = thisDCA.umaData.umaRecipe.FindSlotForVertex(i);
                    if (foundSlot != null)
                    {
                        for (int j = 0; j < SelectedVertexes.Count; j++)
                        {
                            if (SelectedVertexes[j].slot.slotName == foundSlot.slotName && SelectedVertexes[j].vertexIndexOnSlot == i - foundSlot.vertexOffset)
                            {
                                duplicateVertex = true;
                                break;
                            }
                        }
                        if (!duplicateVertex)
                        {
                            SelectedVertexes.Add(new VertexSelection()
                            {
                                vertexIndexOnSlot = i - foundSlot.vertexOffset,
                                slot = foundSlot,
                                WorldPosition = VertexObject.transform.TransformPoint(vertexes[i])
                            });
                        }
                    }
                }
            }
        }
    }

    private bool SingleSelect(Event currentEvent)
    {
        bool found = false;

        Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
        {
            bool duplicateVertex = false;
            if (hit.transform != null && hit.transform.gameObject == VertexObject)
            {
                VertexSelection vs = FindVertex(hit, BakedMesh, VertexObject);
                if (vs != null)
                {

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
                            found = true;
                            SelectedVertexes.Add(vs);
                            selectedVertex = SelectedVertexes.Count - 1;
                        }
                    }
                    else
                    {
                        if (currentEvent.control)
                        {
                            found = false;
                            SelectedVertexes.RemoveAt(selectedVertex);
                            selectedVertex = -1;
                        }
                    }
                }
            }
        }
        return found;
    }

    public void CloseStage()
    {
        // This is only called from the MeshModifierEditor being closed
        // so we need to null this out so we don't try to close it again
        this.modifierEditor = null;
        StageUtility.GoBackToPreviousStage();
        SceneView.RepaintAll();

    }

    

    private void RebuildMesh(bool RecipeChanged)
    {
        UMAGeneratorBuiltin gb = thisDCA.umaData.umaGenerator as UMAGeneratorBuiltin;
        thisDCA.umaData.CharacterUpdated.AddAction(BuildCollisionMesh);
        if (gb != null)
        {
            gb.Clear();
            if (RecipeChanged)
            {
                var suppressed = SaveSuppressedSlots();
                thisDCA.BuildCharacter(true, true);
                //gb.GenerateSingleUMA(thisDCA.umaData, true);
                RestoreSuppressedSlots(suppressed);
            }
            // always have to rebuild because the slots are regenerated
            thisDCA.umaData.Dirty(false, true, true); // have to rebuild materials and mesh if we drop out slots
            gb.GenerateSingleUMA(thisDCA.umaData, true);
        }
    }

    public List<string> SaveSuppressedSlots()
    {
        List<string> suppressed = new List<string>();
        foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
        {
            if (slot.Suppressed)
            {
                suppressed.Add(slot.slotName);
            }
        }
        return suppressed;
    }

    public void RestoreSuppressedSlots(List<string> suppressed)
    {
        foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
        {
            if (suppressed.Contains(slot.slotName))
            {
                slot.Suppressed = true;
            }
        }
    }

    public void BuildCollisionMesh(UMAData umaData)
    {
        Debug.Log("Collision mesh being rebuilt");
        thisDCA.umaData.CharacterUpdated.RemoveAction(BuildCollisionMesh);
        SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
        GameObject.DestroyImmediate(BakedMesh);
        BakedMesh = new Mesh();
        BakedMesh.name = "BakedMesh";
        smr.BakeMesh(BakedMesh, true);
        Debug.Log("Baked mesh has " + BakedMesh.vertexCount + " vertices");
        VertexObject.GetComponent<MeshFilter>().sharedMesh = BakedMesh;
        MeshCollider mc = VertexObject.GetComponent<MeshCollider>();
        Debug.Log("Setting mesh collider");
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
        NeedsCameraSetup = false;

        Tools.current = Tool.None;
        Tools.hidden = true;

        SceneView.CameraMode camMode = sceneView.cameraMode;
        camMode.drawMode = DrawCameraMode.TexturedWire;

        // Setup Scene view state
        sceneView.sceneViewState.showFlares = false;
        sceneView.sceneViewState.alwaysRefresh = false;
        sceneView.sceneViewState.showFog = false;
        sceneView.sceneViewState.showSkybox = false;
        sceneView.sceneViewState.showImageEffects = false;
        sceneView.sceneViewState.showParticleSystems = false;
        sceneView.sceneLighting = false;
        sceneView.cameraMode = camMode;
        sceneView.wantsMouseMove = true;
        sceneView.wantsMouseEnterLeaveWindow = true;

        // this doesn't work in 2021.3
        Tools.hidden = true;

        SceneView.lastActiveSceneView.pivot = new Vector3(0, 1, 2.5f);
        Selection.activeObject = VertexObject;
        sceneView.AlignViewToObject(cameraAnchor.transform);
        sceneView.FrameSelected();
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

    private Material vertexMaterial = null;
    private Material GetVertexMaterial(Color col)
    { 
        if (vertexMaterial != null)
        {
            vertexMaterial.SetColor("_Color", col);
            return vertexMaterial;
        }
        Material M = UMAUtils.GetDefaultDiffuseMaterial();
        M.shader = Shader.Find("UMA/UnlitInstanced");
        M.SetColor("_Color", col);
        vertexMaterial = M;
        return vertexMaterial;
    }

    private Mesh vertexMesh = null;

    private Mesh GetVertexMesh()
    {
        if (vertexMesh == null)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vertexMesh = Instantiate(obj.GetComponent<MeshFilter>().sharedMesh);
            DestroyImmediate(obj);
        }
        return vertexMesh;
    }

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
