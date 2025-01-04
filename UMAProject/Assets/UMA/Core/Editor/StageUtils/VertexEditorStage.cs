using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UMA;
using UMA.CharacterSystem;
using UMA.Editors;
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
    private List<VertexSelection> SelectedVertexes = new List<VertexSelection>();
    PhysicsScene phyScene;

    // Edit Options
    float HandlesSize = 0.01f;
    public Color ActiveColor = new Color32(0, 210, 0, 255);
    public Color InactiveColor = new Color32(235, 0, 0, 255);
    bool  selectObscured = false;
    bool  selectFacingAway = false;
    private GUIStyle centeredLabel;
    private int currentSelected = -1;
    float blinkSpeed = 0.2f;

    enum  selectMode { Add, Remove, InvertSelection, Activate, Deactivate };

    string [] selectFrom = new string[] { "All Slots"};
    int selectionSlot = 0; // 0 is all slots

    selectMode currentMode = selectMode.Add;
    // End Options

    const int VertexEditorToolsWindowID = 0x1234;
    const int VisibleWearablesID = 0x1235;

    public Vector2 VertexEditorScrollLocation = Vector2.zero;
    public Rect VertexEditorToolsWindow = new Rect(10, 10, 250, 300);


    public Vector2 VisibleWearablesLocation = Vector2.zero;
    public Rect VisibleWearablesWindow = new Rect(10, 310, 250, 300);

    private MeshModifierEditor modifierEditor;
    public bool rectSelect = false;
    public bool painting = false;
    public Vector2 RectStart = Vector2.zero;
    public MeshModifier Currentmodifier;
    public Type[] ModifierTypes;

    private enum vertexState
    {
        unKnown,
        Active,
        Inactive,
        AddingOnly
    }

    private enum newVertexState
    {
        Inactive,
        Active
    }

    int currentNewVertexState = 1;

    private vertexState currentState;


    GUIStyle HelpBoxStyle;
    private class VertexSelection
    {
        public int vertexIndexOnSlot;
        public SlotData slot;
        public Vector3 WorldPosition;
        public bool isActive;
        public bool suppressed;
    }

    public HashSet<int> flippedVertexes = new HashSet<int>();

    public static VertexEditorStage ShowStage(DynamicCharacterAvatar DCA, MeshModifier modifier)
    {
        VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
        stage.titleContent = new GUIContent();
        stage.titleContent.text = "Mesh Modifier Editor";
        stage.titleContent.image = EditorGUIUtility.IconContent("GameObject Icon").image;
        stage.thisDCA = DCA;
        stage.Currentmodifier = modifier;
        StageUtility.GoToStage(stage, true); 
        return stage;
    }


    protected override bool OnOpenStage()
    {
        base.OnOpenStage();
        //scene = EditorSceneManager.NewPreviewScene();

        centeredLabel = new GUIStyle(GUI.skin.label);
        centeredLabel.fontStyle = FontStyle.Bold;
        centeredLabel.alignment = TextAnchor.MiddleCenter;

        modifierEditor = MeshModifierEditor.GetOrCreateWindowFromModifier(Currentmodifier,thisDCA, this);
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
        HelpBoxStyle = new GUIStyle(EditorStyles.miniLabel);
        HelpBoxStyle.wordWrap = true;
        //AssetDatabase.StartAssetEditing();


        return true;
    }

    private List<Type> LoadTypes(Type baseType)
    {
        List<Type> theTypes = new List<Type>();
        var Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(p => !p.IsDynamic);

        foreach (var asm in Assemblies)
        {
            var Types = asm.GetExportedTypes();
            foreach (var t in Types)
            {
                if (typeof(VertexAdjustmentCollection).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                {
                    theTypes.Add(t);
                }
            }
        }
        return theTypes;
    }



    protected override void OnCloseStage()
    {
        //AssetDatabase.StopAssetEditing();   
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
            //thisDCA.StartCoroutine(RegenerateUMA());
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

    private IEnumerator RegenerateUMA()
    {
        yield return null; 
        thisDCA.GenerateSingleUMA();
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
    }


    private void DoSceneGUI(SceneView sceneView)
    {
        Handles.SetCamera(sceneView.camera);
        if (!rectSelect && Event.current.alt)
        {
            DrawHandles(SelectedVertexes);
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
            currentState = vertexState.unKnown;

            flippedVertexes.Clear();
            //Debug.Log("Currentevent.button = "+ currentEvent.button);
            if (currentEvent.button == 0)
            {
                if (currentEvent.shift)
                {
                    SingleSelect(currentEvent);
                    rectSelect = false;
                    painting = true;
                }
                else if (currentEvent.control)
                {
                    SingleSelect(currentEvent);
                    rectSelect = false;
                    painting = true;
                }
                else
                {
                    SingleSelect(currentEvent);
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
            painting = false;

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
                painting = false;
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

        DrawHandles(SelectedVertexes);

        // Your custom GUI logic here
        DrawGUIWindows(sceneView);

        // Repaint the scene view only when necessary
        if (currentEvent.type == EventType.Repaint)
        {
            if (painting)
            {
                SingleSelect(currentEvent);
            }
            SceneView.RepaintAll();
        }
    }

    private void DrawHandles(List<VertexSelection> vertexes)
    {
        Color LastColor = Color.black;
        if (EventType.Repaint != Event.current.type)
        {
            return;
        }
        Color saveColor = Handles.color;
        Mesh mesh = GetVertexMesh();
        Material mat = GetVertexMaterial(Color.red);

        Vector3[] normals = BakedMesh.normals;

        HashSet<string> VisibleSlots = new HashSet<string>();
        for(int i=0;i < thisDCA.umaData.umaRecipe.slotDataList.Length; i++)
        {
            SlotData slot = thisDCA.umaData.umaRecipe.slotDataList[i];
            if (!slot.Suppressed)
            {
                VisibleSlots.Add(slot.slotName);
            }
        }



        for (int i = 0; i < vertexes.Count; i++)
        {
            VertexSelection vs = vertexes[i];
            if (vs.suppressed) continue;
            if (!VisibleSlots.Contains(vs.slot.slotName))
            {
                continue;   
            }

            int bakedIndex = vs.vertexIndexOnSlot + vs.slot.vertexOffset;

            Vector3 bakedNormal = normals[bakedIndex]; 
            if (Vector3.Dot(bakedNormal, Camera.current.transform.forward) > 0)
            {
                 continue;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(vs.WorldPosition, Quaternion.identity, Vector3.one * HandlesSize);

            Color newColor = InactiveColor;
          
            if (vs.isActive)
            {
                newColor = ActiveColor;
            }

            if (i == currentSelected)
            {
                AnimationCurve curve = AnimationCurve.EaseInOut(0,0,1,1);
                float time = Time.fixedTime / blinkSpeed;
                float val = curve.Evaluate(time % 1.0f);
                newColor = Color.Lerp(newColor, Color.white, val);
            }

            if (newColor != LastColor)
            {
                LastColor = newColor;
                mat.SetColor("_Color", newColor);
                mat.SetPass(0);
            }
            Graphics.DrawMeshNow(mesh, matrix);
        }
    }

    private void DrawGUIWindows(SceneView sceneView)
    {
        Handles.BeginGUI();

        VertexEditorToolsWindow = GUI.Window(VertexEditorToolsWindowID, VertexEditorToolsWindow, DoToolsWindow, "Tools");

        VisibleWearablesWindow = GUI.Window(VisibleWearablesID, VisibleWearablesWindow, (id) =>
        {
            VisibleWearablesLocation = GUILayout.BeginScrollView(VisibleWearablesLocation);
            bool wasChanged = false;
            bool wasRecipeChanged = false;
            var wearables = thisDCA.GetVisibleWearables();

            GUILayout.Label("Visible Wearables",EditorStyles.boldLabel);
            foreach (var wearable in wearables)
            {
                GUILayout.BeginHorizontal();
                bool wasVisible = wearable.disabled;
                wearable.disabled = !GUILayout.Toggle(!wearable.disabled, "", GUILayout.Width(24));
                if (wasVisible != wearable.disabled)
                {
                    wasRecipeChanged = true;
                }
                GUILayout.Label(wearable.name);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(10);
            GUILayout.Label("Visible Slots", EditorStyles.boldLabel);
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                GUILayout.BeginHorizontal();
                bool wasDisabled = slot.Suppressed;
                // EditorGUI.BeginChangeCheck();  doesn't work here
                slot.Suppressed = !GUILayout.Toggle(!slot.Suppressed, "", GUILayout.Width(24));
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
                RebuildMesh(false);
            }
            if (wasRecipeChanged)
            {
                RebuildMesh(true);
            }
        }, "Visibility");


        Handles.EndGUI();
    }

    public void DoToolsWindow(int ID)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
        GUILayout.Label("Editor Options", centeredLabel);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Handle Size", GUILayout.Width(82));
        HandlesSize = EditorGUILayout.Slider(HandlesSize, 0.0f, 0.04f);
        GUILayout.EndHorizontal();
        GUILayout.Label("Vertex Colors",centeredLabel);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Active", GUILayout.Width(82));
        ActiveColor = EditorGUILayout.ColorField(ActiveColor, GUILayout.Width(90));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Inactive", GUILayout.Width(82));
        InactiveColor = EditorGUILayout.ColorField(InactiveColor, GUILayout.Width(90));
        GUILayout.EndHorizontal();
        GUIHelper.EndVerticalPadded(5);
        GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
        GUILayout.Label("Selection Options", centeredLabel);
        GUILayout.BeginHorizontal();
        selectObscured = EditorGUILayout.Toggle("Obscured", selectObscured);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(!selectObscured);
        selectFacingAway = EditorGUILayout.Toggle("Backfacing", selectFacingAway);
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Default State", GUILayout.Width(72));
        currentNewVertexState = EditorGUILayout.Popup(currentNewVertexState, new string[] { "Inactive", "Active" });
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Drag Mode",GUILayout.Width(72));
        currentMode = (selectMode)EditorGUILayout.EnumPopup(currentMode);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Slot Filter", GUILayout.Width(72));

        if (selectionSlot >= selectFrom.Length)
        {
            selectionSlot = 0;
        }
        selectionSlot = EditorGUILayout.Popup(selectionSlot, selectFrom);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Invert State"))
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                SelectedVertexes[i].isActive = !SelectedVertexes[i].isActive;
            }
        }
        if (GUILayout.Button("Activate all selected"))
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                SelectedVertexes[i].isActive = true;
            }
        }
        if (GUILayout.Button("Deactivate all selected"))
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                SelectedVertexes[i].isActive = false;
            }
        }
        if (GUILayout.Button("Clear Selection"))
        {
            SelectedVertexes.Clear();
        }
        if (GUILayout.Button("Invert Selection"))
        {
            EditorUtility.ClearProgressBar();
            List<VertexSelection> newSelection = new List<VertexSelection>();
            try
            {
                for (int i = 0; i < BakedMesh.vertices.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("Inverting Selection", "Processing vertex " + i.ToString(), (float)i / (float)BakedMesh.vertices.Length);
                    SlotData foundSlot = thisDCA.umaData.umaRecipe.FindSlotForVertex(i);
                    if (foundSlot != null)
                    {
                        int foundVert = i - foundSlot.vertexOffset;
                        bool found = false;
                        for (int j = 0; j < SelectedVertexes.Count; j++)
                        {
                            if (SelectedVertexes[j].slot.slotName == foundSlot.slotName && SelectedVertexes[j].vertexIndexOnSlot == foundVert)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            newSelection.Add(new VertexSelection()
                            {
                                vertexIndexOnSlot = foundVert,
                                slot = foundSlot,
                                WorldPosition = VertexObject.transform.TransformPoint(BakedMesh.vertices[i]),
                                isActive = (currentNewVertexState == (int)newVertexState.Active)
                            });
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            SelectedVertexes = newSelection;
        }
        if (GUILayout.Button("Select All"))
        {
            SelectedVertexes.Clear();
            var vertexes = BakedMesh.vertices;
            var normals = BakedMesh.normals;
            for (int i = 0; i < vertexes.Length; i++)
            {
                SlotData foundSlot = thisDCA.umaData.umaRecipe.FindSlotForVertex(i);
                if (foundSlot != null)
                {
                    SelectedVertexes.Add(new VertexSelection()
                    {
                        vertexIndexOnSlot = i - foundSlot.vertexOffset,
                        slot = foundSlot,
                        WorldPosition = VertexObject.transform.TransformPoint(vertexes[i]),
                        isActive = (currentNewVertexState == (int)newVertexState.Active)
                    });
                }
            }
        }
        GUIHelper.EndVerticalPadded(5);

        //GUILayout.Label("camera: " + sceneView.camera.transform.position.ToString());
        if (GUILayout.Button("Reset Camera"))
        {
            SceneView.lastActiveSceneView.pivot = new Vector3(0, 1, 2.5f);
            Selection.activeObject = VertexObject;
            sceneView.AlignViewToObject(cameraAnchor.transform);
            sceneView.FrameSelected(true);
            sceneView.AlignViewToObject(cameraAnchor.transform);
        }
        GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f));
        GUILayout.TextArea("Shift-Click empty spot to add vertex(es)\nShift-click a selected vertex to toggle active state\nControl-Click to remove vertex(es)\nRight-Click to cancel selection\n\nHold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
        GUIHelper.EndVerticalPadded(5);
    }

    private Rect GetMinMax(Vector2 rectStart, Vector2 rectEnd)
    {
        float xMin = Mathf.Min(rectStart.x, rectEnd.x);
        float xMax = Mathf.Max(rectStart.x, rectEnd.x);
        float yMin = Mathf.Min(rectStart.y, rectEnd.y);
        float yMax = Mathf.Max(rectStart.y, rectEnd.y);

        Rect MinMax = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);

        return MinMax;
    }

    private void RectangleSelect(Event currentEvent, Rect ScreenArea)
    {
        EditorUtility.ClearProgressBar();

        var vertexes = BakedMesh.vertices;
        var normals = BakedMesh.normals;
        for (int i = 0; i < vertexes.Length; i++)
        {
            if (i % 100 == 0)
            {
                EditorUtility.DisplayProgressBar("Selecting Vertices", "Processing vertex " + i.ToString(), (float)i / (float)vertexes.Length);
            }
            Vector3 screenPos = HandleUtility.WorldToGUIPoint(VertexObject.transform.TransformPoint(vertexes[i]));
            if (ScreenArea.Contains(screenPos))
            {
                bool blocked = false;

                if (!selectFacingAway)
                {
                    Vector3 Normal = normals[i];
                    // if the normal is not facing the camera
                    if (Vector3.Dot(Normal, Camera.current.transform.forward) > 0)
                    {
                        continue;
                    }
                }


                if (!selectObscured)
                {
                    // do a raycast here from the camera to the vertex (expanded by the normal * 1.001)
                    Ray ray = HandleUtility.GUIPointToWorldRay(screenPos);
                    if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
                    {
                        if (hit.transform != null && hit.transform.gameObject == VertexObject)
                        {
                            float dist = Mathf.Abs(Vector3.Distance(VertexObject.transform.TransformPoint(vertexes[i]), hit.point));
                            if (dist > 0.001f)
                            {
                                blocked = true;
                            }
                        }
                    }
                }

                if (!blocked)
                {
                    SlotData foundSlot = thisDCA.umaData.umaRecipe.FindSlotForVertex(i);
                    int foundVert = i - foundSlot.vertexOffset;

                    if (selectionSlot > 0)
                    {
                        if (foundSlot.slotName != selectFrom[selectionSlot])
                        {
                            continue;
                        }
                    }

                    if (foundSlot != null)
                    {
                        switch (currentMode)
                        {
                            case selectMode.Add:
                                AddVertex(foundSlot, foundVert);
                                break;
                            case selectMode.Remove:
                                RemoveVertex(foundSlot, foundVert);
                                break;
                            case selectMode.InvertSelection:
                                InvertVertex(foundSlot, foundVert);
                                break;
                            case selectMode.Activate:
                                ActivateVertex(foundSlot, foundVert);
                                break;
                            case selectMode.Deactivate:
                                DeactivateVertex(foundSlot, foundVert);
                                break;
                        }
                    }
                }
            }
        }
        EditorUtility.ClearProgressBar();
    }

    void ActivateVertex(SlotData foundSlot, int foundVert)
    {
        for (int i = 0; i < SelectedVertexes.Count; i++)
        {
            if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
            {
                SelectedVertexes[i].isActive = true;
                return;
            }
        }
    }

    void DeactivateVertex(SlotData foundSlot, int foundVert)
    {
        for (int i = 0; i < SelectedVertexes.Count; i++)
        {
            if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
            {
                SelectedVertexes[i].isActive = false;
                return;
            }
        }
    }


    void AddVertex(SlotData foundSlot, int foundVert)
    {
        for (int i = 0; i < SelectedVertexes.Count; i++)
        {
            if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
            {                
                return;
            }
        }
        SelectedVertexes.Add(new VertexSelection()
        {
            vertexIndexOnSlot = foundVert,
            slot = foundSlot,
            WorldPosition = VertexObject.transform.TransformPoint(BakedMesh.vertices[foundVert + foundSlot.vertexOffset]),
            isActive = (currentNewVertexState == (int)newVertexState.Active)
        });
    }

    void RemoveVertex(SlotData foundSlot, int foundVert)
    {
        for (int i = 0; i < SelectedVertexes.Count; i++)
        {
            if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
            {
                SelectedVertexes.RemoveAt(i);
                return;
            }
        }
    }

    void InvertVertex(SlotData foundSlot, int foundVert)
    {
        for (int i = 0; i < SelectedVertexes.Count; i++)
        {
            if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
            {
                SelectedVertexes.RemoveAt(i);
                return;
            }
        }
        SelectedVertexes.Add(new VertexSelection()
        {
            vertexIndexOnSlot = foundVert,
            slot = foundSlot,
            WorldPosition = VertexObject.transform.TransformPoint(BakedMesh.vertices[foundVert + foundSlot.vertexOffset])
        });
    }


    public void SelectVertexes(VertexAdjustmentCollection[] unsortedAdjustments)
    {
        SelectedVertexes.Clear();
        for (int i = 0; i < unsortedAdjustments.Length; i++)
        {
            VertexAdjustmentCollection vac = unsortedAdjustments[i];
            for (int j = 0; j < vac.vertexAdjustments.Length; j++)
            {
                VertexAdjustment va = vac.vertexAdjustments[j];
                SlotData slot = thisDCA.umaData.umaRecipe.GetSlot(va.slotName);
                if (slot != null)
                {
                    SelectedVertexes.Add(new VertexSelection()
                    {
                        vertexIndexOnSlot = va.vertexIndex,
                        slot = slot,
                        WorldPosition = VertexObject.transform.TransformPoint(BakedMesh.vertices[va.vertexIndex + slot.vertexOffset]),
                        isActive = true
                    });
                }
            }
        }
    }
    private bool SingleSelect(Event currentEvent)
    {
        bool found = false;
        int selectedVertex = -1;

        Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
        {
            bool duplicateVertex = false;
            if (hit.transform != null && hit.transform.gameObject == VertexObject)
            {
                VertexSelection vs = FindVertex(hit, BakedMesh, VertexObject);
                if (vs != null)
                {
                    if (selectionSlot > 0)
                    {
                        if (vs.slot.slotName != selectFrom[selectionSlot])
                        {
                            return false;
                        }
                    }

                    for (int i = 0; i < SelectedVertexes.Count; i++)
                    {
                        if (SelectedVertexes[i].slot.slotName == vs.slot.slotName && SelectedVertexes[i].vertexIndexOnSlot == vs.vertexIndexOnSlot)
                        {
                            if (currentEvent.control || (currentEvent.shift))
                            {
                                if (currentState == vertexState.AddingOnly)
                                {
                                    return false;
                                }

                                if (currentState == vertexState.unKnown)
                                {
                                    if (SelectedVertexes[i].isActive)
                                    {
                                        currentState = vertexState.Inactive;
                                    }
                                    else
                                    {
                                        currentState = vertexState.Active;
                                    }
                                    SelectedVertexes[i].isActive = !SelectedVertexes[i].isActive;
                                }
                                else
                                {
                                    if (currentState == vertexState.Active)
                                    {
                                        SelectedVertexes[i].isActive = true;
                                    }
                                    else
                                    {
                                        SelectedVertexes[i].isActive = false;
                                    }
                                }
                            }
                            // SelectedVertexes[i].isActive = !SelectedVertexes[i].isActive;
                            duplicateVertex = true;
                            selectedVertex = i;
                            break;
                        }
                    }

                    if (!duplicateVertex)
                    {
                        if (currentEvent.shift && (currentState == vertexState.unKnown || currentState == vertexState.AddingOnly))
                        {
                            currentState = vertexState.AddingOnly;
                            found = true;
                            SelectedVertexes.Add(vs);
                            currentSelected = SelectedVertexes.Count - 1;
                        }
                    }
                    else
                    {
                        if (currentEvent.control)
                        {
                            found = false;
                            SelectedVertexes.RemoveAt(selectedVertex);
                            if (currentSelected == selectedVertex)
                            {
                                currentSelected = -1;
                            }
                        }
                        else
                        {
                            if (!currentEvent.shift)
                            {
                                currentSelected = selectedVertex;
                            }
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
        thisDCA.umaData.CharacterUpdated.RemoveAction(BuildCollisionMesh);
        SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
        GameObject.DestroyImmediate(BakedMesh);
        if (smr == null)
        {
            Debug.LogError("No SkinnedMeshRenderer found");
            return;
        }
        BakedMesh = new Mesh();
        BakedMesh.name = "BakedMesh";
        smr.BakeMesh(BakedMesh, true);
        VertexObject.GetComponent<MeshFilter>().sharedMesh = BakedMesh;
        MeshCollider mc = VertexObject.GetComponent<MeshCollider>();
        mc.sharedMesh = BakedMesh;
        UpdateSelections();
    }

    public void UpdateSelections()
    {
        Dictionary<string, SlotData> slotDict = new Dictionary<string, SlotData>();

        foreach (SlotData sd in thisDCA.umaData.umaRecipe.slotDataList)
        {
            if (!sd.Suppressed && !sd.asset.isUtilitySlot)
            {
                slotDict.Add(sd.slotName, sd);
            }
        }


        foreach(VertexSelection vs in SelectedVertexes)
        {
            if (slotDict.ContainsKey(vs.slot.slotName))
            {
                vs.slot = slotDict[vs.slot.slotName];
                vs.suppressed = false;
            }
            else
            {
                vs.suppressed = true;
            }
        }
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
        float maxDist = MathF.Abs(Vector3.Distance(local, verts[foundVert])); //?? Why would this ever be negative? Yet it is!!!

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
                WorldPosition = go.transform.TransformPoint(verts[foundVert]),
                isActive = (currentNewVertexState == (int)newVertexState.Active)
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
        //sceneView.cameraMode = camMode; // this quit working? Now gets an error that the cameraMode is not registered?
        sceneView.wantsMouseMove = true;
        sceneView.wantsMouseEnterLeaveWindow = true;


        List<string> slotnames = new List<string>();
        slotnames.Add("All Slots");
        foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
        {
            string s = slot.slotName;
            slotnames.Add(s);
        }
        selectFrom = slotnames.ToArray();



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
