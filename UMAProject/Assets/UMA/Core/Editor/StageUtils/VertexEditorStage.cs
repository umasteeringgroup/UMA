using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMA;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif

namespace UMA
{
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
        public bool hasSaved = false;
        public DynamicCharacterAvatar thisDCA;
        public Mesh BakedMesh;
        private List<VertexSelection> SelectedVertexes = new List<VertexSelection>();
        PhysicsScene phyScene;

        // Edit Options
        float HandlesSize = 0.01f;
        public Color ActiveColor = new Color32(0, 210, 0, 255);
        public Color InactiveColor = new Color32(235, 0, 0, 255);
        bool selectObscured = false;
        bool selectFacingAway = false;
        private GUIStyle centeredLabel;
        private int currentSelected = -1;
        public int CurrentSelected
        {
            get { return currentSelected; }
            set
            {
                if (editorMode == MeshModifierEditor.EditorMode.VertexAdjustments)
                {
                    currentSelected = value;
                    VertexSelection vs = GetSelectedVertex();
                    modifierEditor.Repaint();
                }
            }
        }
        float blinkSpeed = 0.2f;

        enum selectMode { Add, Remove, InvertSelection, Activate, Deactivate };

        string[] selectFrom = new string[] { "All Slots" };
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



        private List<VertexAdjustment> _adjustments = new List<VertexAdjustment>();

        public List<VertexAdjustment> Adjustments
        {
            get
            {
                return _adjustments;
            }
            set
            {
                _adjustments = value;
            }
        }

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
        public class VertexSelection
        {
            public int vertexIndexOnSlot;
            public SlotData slot;
            public Vector3 WorldPosition;
            public bool isActive;
            public bool suppressed;
        }

        [System.Serializable]
        private class SerializedSelection
        {
            public string slotName;
            public int vertexIndex;
            public bool isActive;
        }

        [System.Serializable]
        private class SerializedSelections
        {
            public List<SerializedSelection> selections = new List<SerializedSelection>();
            public static SerializedSelections FromSelections(List<VertexSelection> selections)
            {
                SerializedSelections ss = new SerializedSelections();
                foreach (var selection in selections)
                {
                    ss.selections.Add(new SerializedSelection()
                    {
                        slotName = selection.slot.slotName,
                        vertexIndex = selection.vertexIndexOnSlot,
                        isActive = selection.isActive
                    });
                }
                return ss;
            }

            public List<VertexSelection> ToSelections(DynamicCharacterAvatar DCA, VertexEditorStage stage)
            {
                List<VertexSelection> newSelections = new List<VertexSelection>();
                foreach (var selection in selections)
                {
                    SlotData slot = DCA.umaData.umaRecipe.GetSlot(selection.slotName);
                    if (slot != null)
                    {
                        newSelections.Add(new VertexSelection()
                        {
                            slot = slot,
                            vertexIndexOnSlot = selection.vertexIndex,
                            isActive = selection.isActive,
                            WorldPosition = stage.GetWorldPosition(slot, selection.vertexIndex)
                        });
                    }
                }
                return newSelections;
            }
        }

        public Vector3 GetWorldPosition(SlotData slot, int vertexIndex)
        {
            return VertexObject.transform.TransformPoint(BakedMesh.vertices[vertexIndex + slot.vertexOffset]);
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

        public VertexSelection GetSelectedVertex()
        {
            if (currentSelected >= 0 && currentSelected < SelectedVertexes.Count)
            {
                return SelectedVertexes[currentSelected];
            }
            return null;
        }

        public VertexSelection GetInternalSelection(VertexAdjustment va)
        {
            if (va == null)
            {
                return null;
            }
            var result = GetSelectedVertex();
            if (result != null)
            {
                return result;
            }
            VertexSelection vs = new VertexSelection();
            vs.slot = thisDCA.umaData.umaRecipe.FindSlot(va.slotName);
            if (vs.slot == null)
            {
                return null;
            }
            vs.isActive = true;
            vs.suppressed = false;
            vs.vertexIndexOnSlot = va.vertexIndex;
            vs.WorldPosition = GetWorldPosition(vs.slot, vs.vertexIndexOnSlot);
            return vs;
        }


        public List<VertexSelection> GetVertexSelections()
        {
            return SelectedVertexes;
        }

        public void SetVertexSelections(List<VertexSelection> selections)
        {
            SelectedVertexes = selections;
        }

        public List<VertexSelection> GetActiveSelectedVertexes()
        {
            List<VertexSelection> active = new List<VertexSelection>();
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].isActive)
                {
                    active.Add(SelectedVertexes[i]);
                }
            }
            return active;
        }
        public List<VertexSelection> GetAllVertexes()
        {
            List<VertexSelection> active = new List<VertexSelection>();
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].isActive)
                {
                    active.Add(SelectedVertexes[i]);
                }
            }
            return active;
        }

        public int GetSelectedVertexCount()
        {
            return SelectedVertexes.Count;
        }

        public int GetActiveSelectedVertexCount()
        {
            int count = 0;
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].isActive)
                {
                    count++;
                }
            }
            return count;
        }

        public void AddVertexAdjustment(VertexAdjustment adjustment)
        {
            Adjustments.Add(adjustment);
        }

        public List<VertexAdjustment> GetVertexAdjustments()
        {
            return Adjustments;
        }

        protected override bool OnOpenStage()
        {
            base.OnOpenStage();
            //scene = EditorSceneManager.NewPreviewScene();

            centeredLabel = new GUIStyle(GUI.skin.label);
            centeredLabel.fontStyle = FontStyle.Bold;
            centeredLabel.alignment = TextAnchor.MiddleCenter;

            modifierEditor = MeshModifierEditor.GetOrCreateWindowFromModifier(Currentmodifier, thisDCA, this);
            if (Currentmodifier != null && Currentmodifier.Modifiers != null)
            {
                modifierEditor.Modifiers = Currentmodifier.EditorModifiers;
                foreach (var newMod in modifierEditor.Modifiers)
                {
                    // get the type of the VertexAdjustment for this collection
                    newMod.AfterLoading();
                    /*
                    Type adjType = Type.GetType(newMod.AdjustmentType);
                    Type colType = Type.GetType(newMod.CollectionType);
                    newMod.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(colType);
                    newMod.TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(adjType);
                    foreach(string json in newMod.JsonAdjustments)
                    {
                        VertexAdjustment va = VertexAdjustment.FromJSON(json);
                        if (va != null)
                        {
                            newMod.adjustments.Add(va);
                        }
                    } */
                }

                foreach (string json in Currentmodifier.AdHocAdjustmentJSON)
                {
                    VertexAdjustment va = VertexAdjustment.FromJSON(json);
                    if (va != null)
                    {
                        Adjustments.Add(va);
                    }
                }
            }
            else
            {
                modifierEditor.Modifiers = new List<MeshModifier.Modifier>();
            }
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
            thisDCA.GenerateSingleUMA();

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
            thisDCA.umaData.manualMeshModifiers = new List<MeshModifier.Modifier>();
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

        private IEnumerator RegenerateUMA()
        {
            yield return null;
            thisDCA.GenerateSingleUMA();
        }

        Int64 FloatToFixed5(float f)
        {
            return (Int64)(f * 100000);
        }

        public Int64 GetPositionKey(Vector3 inVector)
        {
            // convert position to 6 digit fixed point.
            // and then pack it into an Int64
            Int64 posKey = 0;
            Int64 x = FloatToFixed5(inVector.x);
            Int64 y = FloatToFixed5(inVector.y);
            Int64 z = FloatToFixed5(inVector.z);

            posKey = x + (y * 1000000) + (z * 1000000000000);
            return posKey;
        }


#if UMA_BURSTCOMPILE
        [BurstCompile(CompileSynchronously = true)]
#endif
        public void RecalculateNormals()
        {
            BakedMesh.RecalculateNormals();
            BakedMesh.RecalculateTangents();

            return;

            // now go through and average the normals for any duplicate vertexes to smooth the mesh at the seams.
            Dictionary<Int64, List<Vector3>> normals = new Dictionary<long, List<Vector3>>();
            Vector3[] verts = BakedMesh.vertices;
            Vector3[] norms = BakedMesh.normals;

            for (int i = 0; i < verts.Length; i++)
            {
                Int64 posKey = GetPositionKey(verts[i]);
                if (!normals.ContainsKey(posKey))
                {
                    normals.Add(posKey, new List<Vector3>());
                }
                normals[posKey].Add(norms[i]);
            }

            for (int i = 0; i < verts.Length; i++)
            {
                Int64 posKey = GetPositionKey(verts[i]);
                List<Vector3> normList = normals[posKey];
                Vector3 avg = Vector3.zero;
                foreach (Vector3 norm in normList)
                {
                    avg += norm;
                }
                avg /= normList.Count;
                norms[i] = avg;
            }
            BakedMesh.normals = norms;
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
            float halfheight = (r.height / 2) - 45;
            float top1 = 5;
            float top2 = halfheight + 10;
            float left1 = 5;
            float left2 = r.width - width - 35;



            VertexEditorToolsWindow = new Rect(left1, top1, width, halfheight);
            VisibleWearablesWindow = new Rect(left1, top2, width, halfheight);
        }

        Quaternion test = Quaternion.identity;

        private void DoSceneGUI(SceneView sceneView)
        {

            Event currentEvent = Event.current;

            Handles.SetCamera(sceneView.camera);
            if (!rectSelect && Event.current.alt)
            {
                DrawHandles(SelectedVertexes);
                return;
            }

            if (editAdjustment != null && editAdjustment.Gizmo != VertexAdjustmentGizmo.None)
            {
                bool changed = DoGizmoInput();
                if (changed)
                {
                    modifierEditor.Repaint();

                    if (modifierEditor.RebuildOnChanges)
                    {
                        modifierEditor.DoCharacterRebuild();
                    }
                }
            }

            Handles.BeginGUI();

            if (isEditing == false)
            {

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
                    GUI.Box(new Rect(RectStart.x, RectStart.y, currentEvent.mousePosition.x - RectStart.x, currentEvent.mousePosition.y - RectStart.y), "");
                }
            }

            if (isEditing)
            {
                Rect topCenter = new Rect(0, 25, sceneView.position.width, 20);
                GUI.Label(topCenter, "** Edit Mode **", centeredLabel);
            }


            Handles.EndGUI();


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

        private VertexAdjustment editAdjustment;
        private VertexSelection editSelection;

        public void SetActive(VertexAdjustment va)
        {
            editSelection = GetInternalSelection(va);
            editAdjustment = va;
        }

        public bool isEditing
        {
            get
            {
                return editAdjustment != null;
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
            for (int i = 0; i < thisDCA.umaData.umaRecipe.slotDataList.Length; i++)
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

                if (i == currentSelected && editAdjustment == null && editorMode == MeshModifierEditor.EditorMode.VertexAdjustments)
                {
                    // do nothing right now
                    AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                    float time = Time.fixedTime / blinkSpeed;
                    float val = curve.Evaluate(time % 1.0f);
                    newColor = Color.Lerp(Color.cyan, Color.white, val);
                    mat.SetColor("_Color", newColor);
                    mat.SetPass(0);
                    Graphics.DrawMeshNow(mesh, matrix);
                    LastColor = newColor;
                }
                else
                {
                    if (newColor != LastColor)
                    {
                        LastColor = newColor;
                        mat.SetColor("_Color", newColor);
                        mat.SetPass(0);
                    }
                    Graphics.DrawMeshNow(mesh, matrix);
                }
            }

            if (editAdjustment != null && editorMode == MeshModifierEditor.EditorMode.VertexAdjustments)
            {
                AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                float time = Time.fixedTime / blinkSpeed;
                float val = curve.Evaluate(time % 1.0f);
                Color newColor = Color.Lerp(Color.cyan, Color.white, val);
                Matrix4x4 matrix = Matrix4x4.TRS(editSelection.WorldPosition, Quaternion.identity, Vector3.one * HandlesSize);
                mat.SetColor("_Color", newColor);
                mat.SetPass(0);
                Graphics.DrawMeshNow(mesh, matrix);
            }
        }

        private bool DoGizmoInput()
        {
            bool changed = false;
            VertexAdjustmentGizmo gizmo = editAdjustment.Gizmo;

            switch (gizmo)
            {
                case VertexAdjustmentGizmo.Rotate:
                    changed = DoRotationGizmo();
                    break;
                case VertexAdjustmentGizmo.Scale:
                    changed = DoScaleGizmo();
                    break;
                case VertexAdjustmentGizmo.Move:
                    changed = DoTranslateGizmo();
                    break;
            }
            return changed;
        }

        private bool DoRotationGizmo()
        {
            bool changed = false;
            // show an arrow gizmo at the editSelection.WorldPosition, pointing in the direction of the normal
            // when the user clicks on the gizmo, show a rotation handle
            // when the user clicks on the rotation handle, rotate the vertex around the normal
            VertexNormalAdjustment van = editAdjustment as VertexNormalAdjustment;

            if (van != null)
            {
                if (van.bakedNormalSet == false)
                {
                    van.bakedNormal = BakedMesh.normals[editSelection.slot.vertexOffset + editSelection.vertexIndexOnSlot];
                    van.bakedNormalSet = true;
                }

                editSelection.WorldPosition = VertexObject.transform.TransformPoint(BakedMesh.vertices[editSelection.slot.vertexOffset + editSelection.vertexIndexOnSlot]);
                // show an arrow gizmo at the editSelection.WorldPosition, pointing in the direction of the normal
                Handles.color = Color.red;
                Vector3 normal = van.bakedNormal;
                Vector3 worldRotation = VertexObject.transform.TransformVector(normal/*BakedMesh.normals[editSelection.slot.vertexOffset + editSelection.vertexIndexOnSlot]*/);
                Quaternion quaternion = Quaternion.LookRotation(worldRotation) * van.rotation;
                Handles.ArrowHandleCap(0, editSelection.WorldPosition, quaternion, 0.1f, EventType.Repaint);
                //            Handles.ArrowHandleCap(0, editSelection.WorldPosition, Quaternion.LookRotation(worldRotation), 0.1f, EventType.Repaint);

                // show a rotation handle at the editSelection.WorldPosition for van.normal
                Quaternion q = Handles.RotationHandle(van.rotation, editSelection.WorldPosition);
                if (q != van.rotation)
                {
                    van.rotation = q;
                    changed = true;
                }
                //van.SetRotation(Handles.RotationHandle(van.rotation, editSelection.WorldPosition));
            }
            return changed;
        }

        private bool DoScaleGizmo()
        {
            VertexScaleAdjustment vas = editAdjustment as VertexScaleAdjustment;
            if (vas != null)
            {
                UMAData umaData = thisDCA.umaData;
                SlotData slot = thisDCA.umaData.umaRecipe.FindSlot(vas.slotName);

                if (slot == null) return false;

                if (!vas.basePosSet)
                {
                    vas.basePos = slot.asset.meshData.vertices[editSelection.vertexIndexOnSlot];
                    vas.basePosSet = true;
                }
                Vector3 basenormal = slot.asset.meshData.normals[editSelection.vertexIndexOnSlot];

                // show an arrow gizmo at the editSelection.WorldPosition, pointing in the direction of the normal
                Handles.color = Color.red;
                //Vector3 normal = vas.bakedNormal;
                Vector3 worldRotation = VertexObject.transform.TransformVector(BakedMesh.normals[editSelection.slot.vertexOffset + editSelection.vertexIndexOnSlot]);
                Quaternion quaternion = Quaternion.LookRotation(worldRotation);
                // Handles.ArrowHandleCap(0, editSelection.WorldPosition, quaternion, 0.1f, EventType.Repaint);
                //Handles.ArrowHandleCap(0, editSelection.WorldPosition, Quaternion.LookRotation(worldRotation), 0.1f, EventType.Repaint);

                Vector3 Scale = Vector3.one * vas.scale;
                Vector3 newScale = Handles.ScaleHandle(Scale, editSelection.WorldPosition, Quaternion.identity, 0.1f);
                if (Scale != newScale)
                {
                    vas.scale = newScale.z;
                    return true;
                }
            }
            return false;
        }

        private bool DoTranslateGizmo()
        {
            return false;
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

                GUILayout.Label("Visible Wearables", EditorStyles.boldLabel);
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
                    if (slot.Suppressed && editAdjustment != null)
                    {
                        if (editAdjustment.slotName == slot.slotName)
                        {
                            editAdjustment = null;
                        }
                    }
                    GUILayout.Label(slot.slotName);
                    GUILayout.EndHorizontal();

                }
                GUILayout.EndScrollView();
                if (wasChanged)
                {
                    RebuildMesh(false);
                    modifierEditor.Repaint();
                }
                if (wasRecipeChanged)
                {
                    RebuildMesh(true);
                    modifierEditor.Repaint();
                }
            }, "Visibility");


            Handles.EndGUI();
        }

        private Vector2 ToolsPos = new Vector2(0, 0);
        private GUIStyle smallButtonStyle;
        private GUIStyle threeButtonStyle;
        bool doneButton = false;
        public float ToolWindowAreaHeight = 0.0f;
        public MeshModifierEditor.EditorMode editorMode = MeshModifierEditor.EditorMode.VertexAdjustments;

        public void DoToolsWindow(int ID)
        {
            if (!doneButton)
            {
                smallButtonStyle = new GUIStyle(EditorStyles.miniButton);
                threeButtonStyle = new GUIStyle(EditorStyles.miniButton);
                smallButtonStyle.fontSize = 9;
                smallButtonStyle.fixedWidth = 82;
                threeButtonStyle.fontSize = 9;
                threeButtonStyle.fixedWidth = 54;
                doneButton = true;
                ToolWindowAreaHeight = VertexEditorToolsWindow.height;
            }
            ToolsPos = GUILayout.BeginScrollView(ToolsPos);
            GUILayout.BeginArea(new Rect(0, 0, VertexEditorToolsWindow.width - 12, ToolsPos.y + ToolWindowAreaHeight));
            SceneView sceneView = SceneView.lastActiveSceneView;
            #region Editor Options
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            GUILayout.Label("Editor Options", centeredLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Handle Size", GUILayout.Width(82));
            HandlesSize = EditorGUILayout.Slider(HandlesSize, 0.0f, 0.04f);
            GUILayout.EndHorizontal();
            GUILayout.Label("Vertex Colors", centeredLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Active", GUILayout.Width(82));
            ActiveColor = EditorGUILayout.ColorField(ActiveColor, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inactive", GUILayout.Width(82));
            InactiveColor = EditorGUILayout.ColorField(InactiveColor, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            GUIHelper.EndVerticalPadded(5);
            #endregion
            #region Selection Options
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
            GUILayout.Label("Drag Mode", GUILayout.Width(72));
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


            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", threeButtonStyle))
            {
                // Save the vertex selections
                SaveSelections();
            }

            if (GUILayout.Button("Load", threeButtonStyle))
            {
                // Load the vertex selections
                SelectedVertexes.Clear();
                LoadSelections();
                modifierEditor.Repaint();
            }
            if (GUILayout.Button("Append", threeButtonStyle))
            {
                // Append the vertex selections
                LoadSelections();
                modifierEditor.Repaint();
            }
            GUILayout.EndHorizontal();


            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Invert State", smallButtonStyle))
            {
                for (int i = 0; i < SelectedVertexes.Count; i++)
                {
                    SelectedVertexes[i].isActive = !SelectedVertexes[i].isActive;
                }
                modifierEditor.Repaint();
            }
            if (GUILayout.Button("Invert Selection", smallButtonStyle))
            {
                InvertSelection();
                modifierEditor.Repaint();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Activate all", smallButtonStyle))
            {
                for (int i = 0; i < SelectedVertexes.Count; i++)
                {
                    SelectedVertexes[i].isActive = true;
                }
                modifierEditor.Repaint();
            }
            if (GUILayout.Button("Deactivate all", smallButtonStyle))
            {
                for (int i = 0; i < SelectedVertexes.Count; i++)
                {
                    SelectedVertexes[i].isActive = false;
                }
                modifierEditor.Repaint();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", smallButtonStyle))
            {
                SelectAll();
                modifierEditor.Repaint();
            }
            if (GUILayout.Button("Clear Selection", smallButtonStyle))
            {
                SelectedVertexes.Clear();
                modifierEditor.Repaint();
            }
            GUILayout.EndHorizontal();
            GUIHelper.EndVerticalPadded(5);
            #endregion


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
            if (Event.current.type == EventType.Repaint)
            {
                float height = GUILayoutUtility.GetLastRect().yMax;
                ToolWindowAreaHeight = height;
            }
            GUIHelper.EndVerticalPadded(5);
            GUILayout.EndArea();
            GUILayout.Space(ToolWindowAreaHeight + 10);
            GUILayout.EndScrollView();


        }


        public void SelectAll()
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

        private void LoadSelections()
        {
            // clear the progress bar
            EditorUtility.ClearProgressBar();
            // load the selections, and then add them to the SelectedVertexes if they don't already exist
            string path = EditorUtility.OpenFilePanel("Load Selections", "Assets", "json");
            if (path.Length > 0)
            {
                string json = File.ReadAllText(path);
                SerializedSelections ss = JsonUtility.FromJson<SerializedSelections>(json);
                List<VertexSelection> newSelections = ss.ToSelections(thisDCA, this);
                for (int i = 0; i < newSelections.Count; i++)
                {
                    bool found = false;
                    for (int j = 0; j < SelectedVertexes.Count; j++)
                    {
                        if (SelectedVertexes[j].slot.slotName == newSelections[i].slot.slotName && SelectedVertexes[j].vertexIndexOnSlot == newSelections[i].vertexIndexOnSlot)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        SelectedVertexes.Add(newSelections[i]);
                    }
                    // update the progress bar
                    EditorUtility.DisplayProgressBar("Loading Selections", "Processing vertex " + i.ToString(), (float)i / (float)newSelections.Count);
                }
            }
            // close the progress bar
            EditorUtility.ClearProgressBar();
        }

        private void SaveSelections()
        {
            // save the selections to disk
            string path = EditorUtility.SaveFilePanel("Save Selections", "Assets", "Selections", "json");
            if (path.Length > 0)
            {
                SerializedSelections ss = SerializedSelections.FromSelections(SelectedVertexes);
                string json = JsonUtility.ToJson(ss);
                File.WriteAllText(path, json);
            }
        }

        private void InvertSelection()
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
            modifierEditor.Repaint();
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


        public void SelectVertexes(VertexAdjustmentCollection unsortedAdjustments)
        {
            SelectedVertexes.Clear();
            VertexAdjustmentCollection vac = unsortedAdjustments;
            for (int j = 0; j < vac.vertexAdjustments.Count; j++)
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
                                CurrentSelected = SelectedVertexes.Count - 1;
                                SetActive(null);
                            }
                        }
                        else
                        {
                            if (currentEvent.control)
                            {
                                found = false;
                                SelectedVertexes.RemoveAt(selectedVertex);
                                if (CurrentSelected == selectedVertex)
                                {
                                    CurrentSelected = -1;
                                    SetActive(null);
                                }
                            }
                            else
                            {
                                if (!currentEvent.shift)
                                {
                                    CurrentSelected = selectedVertex;
                                    SetActive(null);
                                }
                            }
                        }
                    }
                }
            }
            modifierEditor.Repaint();
            return found;
        }

        public void CloseStage()
        {

            // This is only called from the MeshModifierEditor being closed
            // so we need to null this out so we don't try to close it again
            thisDCA.umaData.CharacterUpdated.RemoveAllListeners();
            thisDCA.umaData.manualMeshModifiers.Clear();
            modifierEditor.DoCharacterRebuild(false, false);
            this.modifierEditor = null;

            StageUtility.GoBackToPreviousStage();
            SceneView.RepaintAll();

        }



        public void RebuildMesh(bool RecipeChanged, bool buildCollisionMesh = true)
        {
            UMAGeneratorBuiltin gb = thisDCA.umaData.umaGenerator as UMAGeneratorBuiltin;
            if (buildCollisionMesh)
            {
                thisDCA.umaData.CharacterUpdated.AddAction(BuildCollisionMesh);
            }
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


            foreach (VertexSelection vs in SelectedVertexes)
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

        public Vector3 InverseTransform(Vector3 point)
        {
            return VertexObject.transform.InverseTransformPoint(point);
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

        public Mesh GetBakedMesh()
        {
            return BakedMesh;
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

        internal void RemoveVertexAdjustment(VertexAdjustment removeMe)
        {
            Adjustments.Remove(removeMe);
        }


    }
}