using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UMA;
using UMA.CharacterSystem;
using UMA.Editors;
using System;
using System.Xml.Serialization;

#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif

namespace UMA
{
    public class MeshModifierEditor : EditorWindow
    {
        public bool RebuildOnChanges = false;
        public List<MeshModifier.Modifier> Modifiers = new List<MeshModifier.Modifier>();
        public List<string> BlendShapes = new List<string>();
        public string[] strBlendShapes = new string[0];
        public List<string> blendShapeSlots = new List<string>();
        public List<bool> blendShapeSlotSelected = new List<bool>();

        bool wasAnimatorEnabled;
        bool wasKeepAnimator;
        bool wasRaceFixup;
        Quaternion wasGlobalRotation = Quaternion.identity;
        Quaternion wasRootRotation = Quaternion.identity;



        public static MeshModifierEditor GetOrCreateWindow(DynamicCharacterAvatar DCA, VertexEditorStage vstage)
        {
            MeshModifierEditor wnd = GetWindow<MeshModifierEditor>(true, "Mesh Modifiers", true);
            wnd.Setup(DCA, vstage, null);
            wnd.titleContent = new GUIContent("Mesh Modifiers");
            return wnd;
        }

        public static MeshModifierEditor GetOrCreateWindowFromModifier(MeshModifier modifier, DynamicCharacterAvatar DCA, VertexEditorStage vstage)
        {
            MeshModifierEditor wnd = GetWindow<MeshModifierEditor>(true, "Mesh Modifiers", true);
            wnd.Setup(DCA, vstage, modifier);
            wnd.titleContent = new GUIContent("Mesh Modifiers");
            return wnd;
        }

        public DynamicCharacterAvatar thisDCA;
        public Dictionary<string, MeshModifier> SlotNameToModifiers = new Dictionary<string, MeshModifier>();
        public bool ShowVisibleSlots = false;
        public bool ShowOptions = false;
        public VertexEditorStage vertexEditorStage;
        public int currentModifierIndex = 0;
        public Type[] ModifierTypes = new Type[0];
        public string[] ModifierTypeNames = new string[0];
        public int selectedType = 0;
        //public VertexAdjustment templateAdjustment = null;
        public VertexAdjustmentCollection templateVertexAdjustmentCollection = null;
        public GUIStyle centeredLabel = new GUIStyle();
        public Color backColor = Color.cyan;
        public bool editingCurrent = false;
        public GUIStyle selectedButton;
        public GUIStyle unselectedButton;
        public enum EditorMode { MeshModifiers, VertexAdjustments, Blendshapes }
        public EditorMode editorMode = EditorMode.VertexAdjustments;

        private bool suppressUndoRebuild = false;
        private bool activeAdjustmentInteractiveUndoArmed = false;
        private bool bulkModifierInteractiveUndoArmed = false;

        private UnityEngine.Object[] GetUndoTargets(bool includeCharacterState = false)
        {
            List<UnityEngine.Object> targets = new List<UnityEngine.Object>();
            targets.Add(this);

            if (vertexEditorStage != null)
            {
                targets.Add(vertexEditorStage);
                if (vertexEditorStage.BakedMesh != null)
                {
                    targets.Add(vertexEditorStage.BakedMesh);
                }
            }

            if (includeCharacterState && thisDCA != null)
            {
                targets.Add(thisDCA);
                if (thisDCA.umaData != null)
                {
                    targets.Add(thisDCA.umaData);
                }
            }

            return targets.ToArray();
        }

        private void RegisterUndoSnapshot(string actionName, bool includeCharacterState = false)
        {
            Undo.RegisterCompleteObjectUndo(GetUndoTargets(includeCharacterState), actionName);
        }

        private void MarkEditorStateDirty(bool includeCharacterState = false)
        {
            EditorUtility.SetDirty(this);
            if (vertexEditorStage != null)
            {
                EditorUtility.SetDirty(vertexEditorStage);
                if (vertexEditorStage.BakedMesh != null)
                {
                    EditorUtility.SetDirty(vertexEditorStage.BakedMesh);
                }
            }
            if (includeCharacterState && thisDCA != null)
            {
                EditorUtility.SetDirty(thisDCA);
                if (thisDCA.umaData != null)
                {
                    EditorUtility.SetDirty(thisDCA.umaData);
                }
            }
        }

        private void HandleInteractiveUndoCapture(ref bool isArmed, string actionName)
        {
            if (Event.current == null)
            {
                return;
            }

            EventType type = Event.current.type;
            EventType rawType = Event.current.rawType;

            if (rawType == EventType.MouseUp || type == EventType.MouseUp || type == EventType.Ignore)
            {
                isArmed = false;
                return;
            }

            if (!isArmed && (type == EventType.MouseDown || type == EventType.KeyDown || type == EventType.ScrollWheel))
            {
                RegisterUndoSnapshot(actionName);
                isArmed = true;
            }

            if (type == EventType.Repaint && GUIUtility.hotControl == 0)
            {
                isArmed = false;
            }
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnUndoRedoPerformed()
        {
            if (suppressUndoRebuild)
            {
                return;
            }

            Repaint();
            SceneView.RepaintAll();

            if (thisDCA == null || vertexEditorStage == null)
            {
                return;
            }

            suppressUndoRebuild = true;
            try
            {
                DoCharacterRebuild();
            }
            finally
            {
                suppressUndoRebuild = false;
            }
        }

        public void Setup(DynamicCharacterAvatar DCA, VertexEditorStage vstage, MeshModifier modifier)
        {
            thisDCA = DCA;
            wasKeepAnimator = DCA.KeepAnimatorController;
            wasAnimatorEnabled = DCA.gameObject.GetComponent<Animator>().enabled;
            wasRaceFixup = DCA.activeRace.data.FixupRotations;

            Transform rootTransform = DCA.umaData.skeleton.GetRootTransform();
            Transform globalTransform = DCA.umaData.skeleton.GetGlobalTransform();

            wasGlobalRotation = globalTransform.localRotation;
            wasRootRotation = rootTransform.localRotation;

            SlotNameToModifiers.Clear();
            vertexEditorStage = vstage;
            ModifierTypes = AppDomain.CurrentDomain.GetAllDerivedTypes(typeof(VertexAdjustmentCollection));
            ModifierTypeNames = new string[ModifierTypes.Length];
            for (int i = 0; i < ModifierTypes.Length; i++)
            {
                ModifierTypeNames[i] = ObjectNames.NicifyVariableName(ModifierTypes[i].Name); 
                ModifierTypeNames[i] = ModifierTypeNames[i].Replace(" Collection", "");
            }

            if (modifier == null)
            {
                // create a new modifier?
                Modifiers = new List<MeshModifier.Modifier>();
            }
            else
            {
                currentModifierIndex = 0;
                if (modifier.EditorModifiers != null && modifier.EditorModifiers.Count > 0)
                {
                    Modifiers = modifier.EditorModifiers;
                }
                else if (modifier.Modifiers != null)
                {
                    Modifiers = modifier.Modifiers;
                }
                else
                {
                    Modifiers = new List<MeshModifier.Modifier>();
                }
            }
            // vertexEditorStage = VertexEditorStage.ShowStage(DCA);
            centeredLabel = EditorStyles.boldLabel;
            centeredLabel.alignment = TextAnchor.MiddleCenter;
            selectedButton = new GUIStyle(EditorStyles.miniButton);
            unselectedButton = new GUIStyle(EditorStyles.miniButton);
            selectedButton.normal.textColor = Color.white;
            selectedButton.normal.background = new Texture2D(1, 1);
            selectedButton.normal.background.SetPixel(0, 0, Color.blue);
            selectedButton.normal.background.Apply();
            unselectedButton.normal.textColor = Color.black;
            unselectedButton.normal.background = new Texture2D(1, 1);
            unselectedButton.normal.background.SetPixel(0, 0, Color.white);
            unselectedButton.normal.background.Apply();
        }
        
        private bool IncludeAdHocAdjustments = true;
        private bool IncludeActiveOnlyBulk = true;
        private bool IncludeBulkModifiers = true;

        public void OnGUI()
        {
            if (thisDCA == null)
            {
                EditorGUILayout.LabelField("No DCA selected");
                return;
            }

            GUIStyle VertexModeStyle = unselectedButton;
            GUIStyle MeshModifierModeStyle = unselectedButton;
            GUIStyle BlendshapeStyle = unselectedButton;

            if (editorMode == EditorMode.MeshModifiers)
            {
                MeshModifierModeStyle = selectedButton;
            }
            else if (editorMode == EditorMode.VertexAdjustments)
            {
                VertexModeStyle = selectedButton;
            }
            else
            {
                BlendshapeStyle = selectedButton;
            }
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            GUILayout.Label("Modifiers", centeredLabel);


            bool newIncludeAdHocAdjustments = GUILayout.Toggle(IncludeAdHocAdjustments, "Include Ad-Hoc adjustments");
            if (newIncludeAdHocAdjustments != IncludeAdHocAdjustments)
            {
                RegisterUndoSnapshot("Toggle Include Ad-Hoc Adjustments");
                IncludeAdHocAdjustments = newIncludeAdHocAdjustments;
                MarkEditorStateDirty();
            }

            bool newIncludeBulkModifiers = GUILayout.Toggle(IncludeBulkModifiers, "Include Bulk Modifiers");
            if (newIncludeBulkModifiers != IncludeBulkModifiers)
            {
                RegisterUndoSnapshot("Toggle Include Bulk Modifiers");
                IncludeBulkModifiers = newIncludeBulkModifiers;
                if (IncludeBulkModifiers == false)
                {
                    IncludeActiveOnlyBulk = false;
                }
                MarkEditorStateDirty();
            }

            bool newIncludeActiveOnlyBulk = GUILayout.Toggle(IncludeActiveOnlyBulk, "Only Active Bulk Modifier");
            if (newIncludeActiveOnlyBulk != IncludeActiveOnlyBulk)
            {
                RegisterUndoSnapshot("Toggle Include Active Bulk Modifier");
                IncludeActiveOnlyBulk = newIncludeActiveOnlyBulk;
                MarkEditorStateDirty();
            }

            bool newRebuildOnChanges = GUILayout.Toggle(RebuildOnChanges, "Rebuild on changes");
            if (newRebuildOnChanges != RebuildOnChanges)
            {
                RegisterUndoSnapshot("Toggle Rebuild On Changes");
                RebuildOnChanges = newRebuildOnChanges;
                MarkEditorStateDirty();
            }

            if (GUILayout.Button("Rebuild Now"))
            {
                RegisterUndoSnapshot("Rebuild Character", true);
                DoCharacterRebuild();
                MarkEditorStateDirty(true);
            }
            if (GUILayout.Button("Rebuild to TPose"))
            {
                RegisterUndoSnapshot("Rebuild Character To T-Pose", true);
                DoCharacterRebuild(true);
                MarkEditorStateDirty(true);
            }

            if (GUILayout.Button("Reset Build"))
            {
                RegisterUndoSnapshot("Reset Character Build", true);
                DoCharacterReset();
                MarkEditorStateDirty(true);
            }
            if (GUILayout.Button("Save to Asset"))
            {
                // Get the name of the new asset to save it to.
                // Create a new MeshModifier asset and save it.
                // split all the modifiers (ad-hoc and bulk) into a list of modifiers.
                // save the list of modifiers to the asset.

                SaveToAsset();
            }
            if (GUILayout.Button("Recalculate Normals"))
            {
                RegisterUndoSnapshot("Recalculate Normals");
                vertexEditorStage.RecalculateNormals();
                MarkEditorStateDirty();
            }

            if (IncludeBulkModifiers == false)
            {
                IncludeActiveOnlyBulk = false;
            }

            GUIHelper.EndVerticalPadded(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ad-hoc Adjustments", VertexModeStyle))
            {
                RegisterUndoSnapshot("Switch To Ad-Hoc Adjustments");
                editorMode = EditorMode.VertexAdjustments;
                bulkModifierInteractiveUndoArmed = false;
                activeAdjustmentInteractiveUndoArmed = false;
                MarkEditorStateDirty();
            }
            if (GUILayout.Button("Bulk Add Active", MeshModifierModeStyle))
            {
                RegisterUndoSnapshot("Switch To Bulk Modifiers");
                editorMode = EditorMode.MeshModifiers;
                bulkModifierInteractiveUndoArmed = false;
                activeAdjustmentInteractiveUndoArmed = false;
                deActivateCurrentSelection();
                vertexEditorStage.SetActive(null);
                MarkEditorStateDirty();
            }
            if (GUILayout.Button("Extract Blendshapes", BlendshapeStyle))
            {
                RegisterUndoSnapshot("Switch To Blendshape Extractor");
                editorMode = EditorMode.Blendshapes;
                bulkModifierInteractiveUndoArmed = false;
                activeAdjustmentInteractiveUndoArmed = false;
                deActivateCurrentSelection();
                vertexEditorStage.SetActive(null);
                MarkEditorStateDirty();
            }
            vertexEditorStage.editorMode = editorMode;
            GUILayout.EndHorizontal();

            if (editorMode == EditorMode.MeshModifiers)
            {
                DrawMeshModifiers();
            }
            else if (editorMode == EditorMode.VertexAdjustments)
            {
                DrawAdHocAdjustments();
            }
            else
            {
                DrawBlendshapeExtractor();
            }
        }

        public void SaveToAsset()
        {
            string Path = EditorUtility.SaveFilePanelInProject("Save MeshModifier", "MeshModifier", "asset", "Save current MeshModifier to project");
            if (Path != "")
            {
                string BaseName = System.IO.Path.GetFileNameWithoutExtension(Path);
                MeshModifier meshModifier = CustomAssetUtility.ReplaceAsset<MeshModifier>(Path, false);
                meshModifier.Modifiers = DoModifierSplit(false);
                foreach (MeshModifier.Modifier mod in Modifiers)
                {
                    mod.BeforeSaving();
                }
                meshModifier.EditorModifiers = Modifiers;
                meshModifier.AdHocAdjustmentJSON = new List<string>();
                foreach (VertexAdjustment va in vertexEditorStage.GetVertexAdjustments())
                {
                    meshModifier.AdHocAdjustmentJSON.Add(JsonUtility.ToJson(va));
                }
                EditorUtility.SetDirty(meshModifier);
                AssetDatabase.SaveAssetIfDirty(meshModifier);
            }
        }

        VertexAdjustment activeAdjustment = null;
        public void SetActive(VertexAdjustment va, bool activeState = true, bool registerUndo = false)
        {
            if (registerUndo)
            {
                RegisterUndoSnapshot(activeState ? "Activate Vertex Adjustment" : "Deactivate Vertex Adjustment");
            }
            deActivateCurrentSelection();
            
            va.active = activeState;
            if (activeState)
            {
                vertexEditorStage.SetActive(va);
                activeAdjustment = va;
            }
            else
            {
                vertexEditorStage.SetActive(null);
                activeAdjustment = null;
            }
            if (registerUndo)
            {
                MarkEditorStateDirty();
            }
        }

        private List<bool> ExpandList(List<bool> theList, int newSize)
        {
            if (theList.Count < newSize)
            {
                while (theList.Count < newSize)
                {
                    theList.Add(false);
                }
            }
            return theList;
        }

        public void SetExpanded(List<bool> theList, int active)
        {
            for (int i = 0; i < theList.Count; i++)
            {
                theList[i] = false;
            }
            theList[active] = true;
        }
        //public Dictionary<int, bool> VertexFoldouts = new Dictionary<int, bool>();

        private Vector2 vertexScrollPos = new Vector2();

        public List<bool> FoldOuts = new List<bool>();
        private VertexAdjustmentCollection templateCollection = null;
        private int currentBlendshape = 0;
        private string[] dnaNames = new string[0];
        private int currentDNA = 0;

        private void DrawBlendshapeExtractor()
        {
            // Allow to select the blendshape
            // Allow to select the slots to add.
            // Allow to select the DNA to drive it (or use "Manual").
            // foreach slot, 
            //    extract Blendshape
            //    create a modifier for it.
            //    add all the vertexes which have changes from the base.
            EditorGUILayout.HelpBox("Blendshape Extraction allows you to create a MeshModifier that acts as a Blendshape. You can assign it to DNA to vary the blendshape value", MessageType.Info);
            if (BlendShapes.Count == 0)
            {
                var Renderer = thisDCA.umaData.GetRenderer(0);
                if (Renderer == null)
                {
                    EditorGUILayout.HelpBox("No Renderer was found on this character", MessageType.Warning);
                    return;
                }
                if (Renderer.sharedMesh.blendShapeCount < 1)
                {
                    EditorGUILayout.HelpBox("No blendshapes were found on this renderer! Please turn on blendshapes and reconstruct character", MessageType.Warning);
                }
                for (int i = 0; i < Renderer.sharedMesh.blendShapeCount; i++)
                {
                    string blShape = Renderer.sharedMesh.GetBlendShapeName(i);
                    BlendShapes.Add(blShape);
                }
                strBlendShapes = BlendShapes.ToArray();

                blendShapeSlots = new List<string>();
                blendShapeSlotSelected = new List<bool>();
                foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (slot != null)
                    {
                        blendShapeSlots.Add(slot.slotName);
                        blendShapeSlotSelected.Add(false);
                    }
                }
                var dnaList = thisDCA.activeRace.data.GetDNANames();
                dnaList.Insert(0, "Manual");
                dnaNames = dnaList.ToArray();
            }
            
            int newCurrentBlendshape = EditorGUILayout.Popup("Select Blendshape", currentBlendshape, strBlendShapes);
            if (newCurrentBlendshape != currentBlendshape)
            {
                RegisterUndoSnapshot("Select Blendshape");
                currentBlendshape = newCurrentBlendshape;
                MarkEditorStateDirty();
            }

            int newCurrentDNA = EditorGUILayout.Popup("Select DNA", currentDNA, dnaNames);
            if (newCurrentDNA != currentDNA)
            {
                RegisterUndoSnapshot("Select Blendshape DNA");
                currentDNA = newCurrentDNA;
                MarkEditorStateDirty();
            }

            GUIHelper.BeginVerticalPadded();
            GUILayout.Label("Select Slots to extract blendshapes",centeredLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                RegisterUndoSnapshot("Select All Blendshape Slots");
                for (int i = 0; i < blendShapeSlotSelected.Count; i++)
                {
                    blendShapeSlotSelected[i] = true;
                }
                MarkEditorStateDirty();
            }
            if (GUILayout.Button("Clear Selection"))
            {
                RegisterUndoSnapshot("Clear Blendshape Slot Selection");
                for (int i = 0; i < blendShapeSlotSelected.Count; i++)
                {
                    blendShapeSlotSelected[i] = false;
                }
                MarkEditorStateDirty();
            }
            GUILayout.EndHorizontal();
            for (int i = 0; i < blendShapeSlots.Count; i++)
            {
                bool newSelectedState = EditorGUILayout.Toggle(blendShapeSlots[i], blendShapeSlotSelected[i]);
                if (newSelectedState != blendShapeSlotSelected[i])
                {
                    RegisterUndoSnapshot("Toggle Blendshape Slot");
                    blendShapeSlotSelected[i] = newSelectedState;
                    MarkEditorStateDirty();
                }
            }
            GUIHelper.EndVerticalPadded();
            if (GUILayout.Button("Extract Blendshapes"))
            {
                RegisterUndoSnapshot("Extract Blendshapes");
                ExtractBlendshapes(strBlendShapes[currentBlendshape],dnaNames[currentDNA],blendShapeSlotSelected,blendShapeSlots);
                MarkEditorStateDirty();
            }
            GUIHelper.BeginVerticalPadded();
            foreach(var mod in Modifiers)
            {
                if (mod == null || mod.TemplateAdjustment == null)
                {
                    continue;
                }
                if (mod.TemplateAdjustment.GetType() == typeof(VertexBlendshapeAdjustment))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Blendshape: {mod.ModifierName} Slot: {mod.SlotName}");
                    if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        RegisterUndoSnapshot("Remove Extracted Blendshape Modifier");
                        Modifiers.Remove(mod);
                        MarkEditorStateDirty();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUIHelper.EndVerticalPadded();
        }

        private void ExtractBlendshapes(string blendShapeName, string dnaName, List<bool> selected, List<string> slots)
        {
            foreach (var strSlot in slots)
            {
                if (!selected[slots.IndexOf(strSlot)])
                {
                    continue;
                }

                SlotData sd = thisDCA.umaData.umaRecipe.GetSlot(strSlot);
                if (sd == null)
                {
                    // ??
                    continue;
                }
                if (sd.asset.meshData.blendShapes == null)
                {
                    continue;
                }
                if (sd.asset.meshData.blendShapes.Length == 0)
                {
                    continue;
                }
                UMABlendShape foundShape = null;

                foreach (var bs in sd.asset.meshData.blendShapes)
                {
                    if (bs.shapeName == blendShapeName)
                    {
                        foundShape = bs;
                        break;
                    }
                }
                if (foundShape == null)
                {
                    continue;
                }

                // found the blendshape for this slot.
                // if a blendShapeModifier for this already exists, delete it.
                for (int i = 0; i < Modifiers.Count; i++)
                {
                    if (Modifiers[i].ModifierName == blendShapeName && Modifiers[i].SlotName == strSlot)
                    {
                        Modifiers.RemoveAt(i);
                        break;
                    }
                }


                int maxFrame = foundShape.frames.Length - 1;
                UMABlendFrame frame = foundShape.frames[maxFrame];
                if (frame != null)
                {
                    // create the new modifier
                    MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                    newMod.ModifierName = blendShapeName;
                    newMod.DNAName = dnaName;
                    newMod.Scale = 1.0f;
                    newMod.SlotName = strSlot;
                    newMod.keepAsIs = true;
                    newMod.adjustments = new VertexBlendshapeAdjustmentCollection();
                    newMod.TemplateAdjustment = new VertexBlendshapeAdjustment();
                    for (int i = 0; i < frame.deltaVertices.Length; i++)
                    {
                        if (frame.deltaVertices[i] != Vector3.zero)
                        {
                            VertexBlendshapeAdjustment vba = new VertexBlendshapeAdjustment();
                            vba.vertexIndex = i;
                            vba.slotName = strSlot;
                            vba.vertexIndex = i;
                            vba.delta = frame.deltaVertices[i];

                            if (frame.HasTangents())
                            {
                                vba.tangent = frame.deltaTangents[i];
                            }
                            else
                            {
                                vba.tangent = Vector3.zero;
                            }
                            if (frame.HasNormals())
                            {
                                vba.normal = frame.deltaNormals[i];
                            }
                            else
                            {
                                vba.normal = Vector3.zero;
                            }
                            newMod.adjustments.Add(vba);
                        }
                    }

                    Modifiers.Add(newMod);
                    currentModifierIndex = Modifiers.Count - 1;
                }
            }
        }


        private void DrawAdHocAdjustments()
        {
            int activeCount = 0;
            bool allowAdd = true;

            if (vertexEditorStage.CurrentSelected < 0 && allowAdd)
            {
                //Debug.Log("No vertexes selected. CurrentSelect <= 0");
                EditorGUILayout.LabelField("No Current Vertex", centeredLabel);
                EditorGUILayout.HelpBox("Please click one of the selected vertexes in the scene view to edit it. The vertex can be active or inactive.", MessageType.Info);
                allowAdd = false;
                //return;
            }
            
            VertexEditorStage.VertexSelection selectedVertex = vertexEditorStage.GetSelectedVertex();
            if (selectedVertex == null && allowAdd)
            {
                EditorGUILayout.LabelField("No Current Vertex", centeredLabel);
                EditorGUILayout.HelpBox("Please click one of the selected vertexes in the scene view to edit it. The vertex can be active or inactive.", MessageType.Info);
                //return;
                allowAdd = false;
            }
            if (selectedVertex != null && selectedVertex.suppressed && allowAdd)
            {
                EditorGUILayout.LabelField("Vertex is suppressed", centeredLabel);
                EditorGUILayout.HelpBox("This vertex is suppressed and cannot be edited. A vertex is suppressed when the slot it is on is hidden.", MessageType.Info);
                allowAdd = false;
                // return;
            }

            /* GUILayout.BeginHorizontal();
              if (GUILayout.Button("Show All", (showFiltered == false? unselectedButton:selectedButton)))
              {
                  showFiltered = false;
                  Repaint();
              }
              if (GUILayout.Button("Show Filtered", (showFiltered == true ? unselectedButton : selectedButton)))
              {
                  showFiltered = true;
                  Repaint();
              }

              GUILayout.EndHorizontal();
            */
            if (allowAdd)
            {
                EditorGUILayout.LabelField("Add Vertex Modifier", centeredLabel);

                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Type", GUILayout.Width(60));
                int newSelectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames, GUILayout.Width(180));
                if (newSelectedType != selectedType)
                {
                    RegisterUndoSnapshot("Change Adjustment Type");
                    selectedType = newSelectedType;
                    MarkEditorStateDirty();
                }
                if (selectedType >= 0)
                {
                    if (templateCollection == null || templateCollection.GetType() != ModifierTypes[selectedType])
                    {
                        templateCollection = (VertexAdjustmentCollection)Activator.CreateInstance(ModifierTypes[selectedType]);
                    }
                }

                if (templateVertexAdjustmentCollection == null || ModifierTypes[selectedType] != templateVertexAdjustmentCollection.GetType() && selectedType < ModifierTypes.Length)
                {
                    templateVertexAdjustmentCollection = (VertexAdjustmentCollection)Activator.CreateInstance(ModifierTypes[selectedType]);
                }
                if (GUILayout.Button("Add"))
                {
                    RegisterUndoSnapshot("Add Vertex Adjustment");
                    VertexAdjustment va = CreateVertexAdjustment(selectedVertex, templateVertexAdjustmentCollection);
                    int newSize = vertexEditorStage.GetVertexAdjustments().Count + 1;
                    FoldOuts = ExpandList(FoldOuts, newSize);
                    SetExpanded(FoldOuts, newSize - 1);
                    vertexEditorStage.AddVertexAdjustment(va);
                    SetActive(va);
                    MarkEditorStateDirty();
                }
                GUILayout.EndHorizontal();
                if (templateCollection != null)
                {
                    EditorGUILayout.HelpBox(templateCollection.Help, MessageType.Info);
                }

                GUIHelper.EndVerticalPadded(10);
            }

            GUILayout.Label("Adjustments", centeredLabel);

            vertexScrollPos = EditorGUILayout.BeginScrollView(vertexScrollPos);

            VertexAdjustment RemoveMe = null;
            int pos = 0;
            var adjustments = vertexEditorStage.GetVertexAdjustments();
            //FoldOuts = ExpandList(FoldOuts, adjustments.Count);

            if (activeAdjustment != null)
            {
                ShowActiveAdjustment(activeCount, activeAdjustment);
            }
            else
            {

            }

            foreach (VertexAdjustment va in adjustments)
            {
                //if (showFiltered && (selectedVertex.vertexIndexOnSlot != va.vertexIndex || selectedVertex.slot.slotName != va.slotName))
                //{
                //    continue;
                //}

                bool delme = false;
                //FoldOuts[pos] = GUIHelper.FoldoutBarWithDelete(FoldOuts[pos], $"{va.slotName},{va.vertexIndex},{va.Name}", out delme);
                GUILayout.BeginHorizontal();
                if (va == activeAdjustment)
                {
                    GUILayout.Label("(edit)", GUILayout.Width(64));
                }
                else
                {
                    GUILayout.Label(" ", GUILayout.Width(64));
                }
                if(GUILayout.Button($"{va.slotName},{va.vertexIndex},{va.Name}", EditorStyles.miniButtonMid, GUILayout.ExpandWidth(true)))
                {
                    SetActive(va, true, true);
                }
                delme = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();

                if (delme)
                {
                    RemoveMe = va;
                    if (activeAdjustment == va)
                    {
                        SetActive(va,false);
                    }

                }

                //if (FoldOuts[pos])
                //{
                //    activeCount = ShowActiveAdjustment(activeCount, va);
                //}
                pos++;
            }
            //if (activeCount == 0)
            //{
            //    vertexEditorStage.SetActive(null);
            //}
            if (RemoveMe != null)
            {
                RegisterUndoSnapshot("Remove Vertex Adjustment");
                vertexEditorStage.RemoveVertexAdjustment(RemoveMe);
                if (RebuildOnChanges)
                {
                    DoCharacterRebuild();
                    //DoCharacterRebuildWithUpdates();
                }
                MarkEditorStateDirty(true);
            }
            EditorGUILayout.EndScrollView();
        }

        private VertexAdjustment CreateVertexAdjustment(VertexEditorStage.VertexSelection selectedVertex, VertexAdjustmentCollection collection)
        {
            VertexAdjustment va = collection.Create();
            va.vertexIndex = selectedVertex.vertexIndexOnSlot;
            va.slotName = selectedVertex.slot.slotName;
            va.active = true;
            va.Init(selectedVertex.slot.asset.meshData);
            return va;
        }


        public List<MeshModifier.Modifier> DoModifierSplit(bool useBuildOptions)
        {
            List<MeshModifier.Modifier> result = new List<MeshModifier.Modifier>();
            if (IncludeAdHocAdjustments || useBuildOptions == false)
            {
                Dictionary<string, MeshModifier.Modifier> testModifiers = new Dictionary<string, MeshModifier.Modifier>();

                foreach (VertexAdjustment va in vertexEditorStage.GetVertexAdjustments())
                {
                    string key = va.Name + ":" + va.slotName;
                    if (!testModifiers.ContainsKey(key))
                    {
                        MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                        newMod.adjustments = va.VertexAdjustmentCollection;
                        newMod.SlotName = va.slotName;
                        newMod.ModifierName = va.Name;
                        testModifiers.Add(key, newMod);
                    }
                    testModifiers[key].adjustments.Add(va);
                }

                foreach (KeyValuePair<string, MeshModifier.Modifier> kvp in testModifiers)
                {
                    SplitModifiersBySlot(result, kvp.Value);
                }
            }

            if (useBuildOptions)
            {
                if (IncludeBulkModifiers)
                {
                    if (IncludeActiveOnlyBulk)
                    {
                        if (currentModifierIndex < Modifiers.Count && currentModifierIndex >= 0)
                        {
                            SplitModifiersBySlot(result, Modifiers[currentModifierIndex]);
                        }
                    }
                    else
                    {
                        foreach (MeshModifier.Modifier mod in Modifiers)
                        {
                            SplitModifiersBySlot(result, mod);
                        }
                    }
                }
            }
            else
            {
                foreach (MeshModifier.Modifier mod in Modifiers)
                {
                    SplitModifiersBySlot(result, mod);
                }
            }

            ValidateSplitAccounting(result, useBuildOptions);
            return result; 
        }

        private List<VertexAdjustment> GetSourceAdjustmentsForSplit(bool useBuildOptions)
        {
            List<VertexAdjustment> sourceAdjustments = new List<VertexAdjustment>();

            if ((IncludeAdHocAdjustments || useBuildOptions == false) && vertexEditorStage != null)
            {
                var adHoc = vertexEditorStage.GetVertexAdjustments();
                if (adHoc != null)
                {
                    sourceAdjustments.AddRange(adHoc);
                }
            }

            if (useBuildOptions)
            {
                if (IncludeBulkModifiers)
                {
                    if (IncludeActiveOnlyBulk)
                    {
                        if (currentModifierIndex >= 0 && currentModifierIndex < Modifiers.Count)
                        {
                            AddModifierAdjustments(sourceAdjustments, Modifiers[currentModifierIndex]);
                        }
                    }
                    else
                    {
                        foreach (MeshModifier.Modifier mod in Modifiers)
                        {
                            AddModifierAdjustments(sourceAdjustments, mod);
                        }
                    }
                }
            }
            else
            {
                foreach (MeshModifier.Modifier mod in Modifiers)
                {
                    AddModifierAdjustments(sourceAdjustments, mod);
                }
            }

            return sourceAdjustments;
        }

        private static void AddModifierAdjustments(List<VertexAdjustment> buffer, MeshModifier.Modifier modifier)
        {
            if (buffer == null || modifier == null || modifier.adjustments == null || modifier.adjustments.vertexAdjustments == null)
            {
                return;
            }

            buffer.AddRange(modifier.adjustments.vertexAdjustments);
        }

        private static string BuildAdjustmentAccountingKey(VertexAdjustment adjustment)
        {
            return adjustment.slotName + "|" + adjustment.vertexIndex + "|" + adjustment.GetType().FullName;
        }

        private static Dictionary<string, int> BuildAdjustmentCountMap(List<VertexAdjustment> adjustments)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (adjustments == null)
            {
                return counts;
            }

            foreach (VertexAdjustment adjustment in adjustments)
            {
                if (adjustment == null || string.IsNullOrEmpty(adjustment.slotName))
                {
                    continue;
                }

                string key = BuildAdjustmentAccountingKey(adjustment);
                if (counts.ContainsKey(key))
                {
                    counts[key]++;
                }
                else
                {
                    counts.Add(key, 1);
                }
            }
            return counts;
        }

        private void ValidateSplitAccounting(List<MeshModifier.Modifier> result, bool useBuildOptions)
        {
            List<VertexAdjustment> sourceAdjustments = GetSourceAdjustmentsForSplit(useBuildOptions);
            List<VertexAdjustment> resultAdjustments = new List<VertexAdjustment>();
            if (result != null)
            {
                foreach (MeshModifier.Modifier modifier in result)
                {
                    AddModifierAdjustments(resultAdjustments, modifier);
                }
            }

            Dictionary<string, int> sourceCounts = BuildAdjustmentCountMap(sourceAdjustments);
            Dictionary<string, int> resultCounts = BuildAdjustmentCountMap(resultAdjustments);

            int sourceTotal = 0;
            foreach (var kvp in sourceCounts)
            {
                sourceTotal += kvp.Value;
            }

            int resultTotal = 0;
            foreach (var kvp in resultCounts)
            {
                resultTotal += kvp.Value;
            }

            if (sourceTotal != resultTotal)
            {
                Debug.LogError($"MeshModifier split validation mismatch. Source adjustments: {sourceTotal}, Result adjustments: {resultTotal}");
            }

            foreach (var kvp in sourceCounts)
            {
                int resultCount = resultCounts.ContainsKey(kvp.Key) ? resultCounts[kvp.Key] : 0;
                if (resultCount != kvp.Value)
                {
                    Debug.LogError($"MeshModifier split lost or duplicated adjustments for key '{kvp.Key}'. Source: {kvp.Value}, Result: {resultCount}");
                }
            }

            foreach (var kvp in resultCounts)
            {
                if (!sourceCounts.ContainsKey(kvp.Key))
                {
                    Debug.LogError($"MeshModifier split produced unexpected adjustment key '{kvp.Key}' ({kvp.Value} entries)");
                }
            }
        }

#if UMA_BURSTCOMPILE
        [BurstCompile(CompileSynchronously = true)]
#endif
        public void DoCharacterRebuild(bool forceTPose = false, bool buildCollisionMesh=true, bool LoadMeshModifiers = true)
        {
            if (forceTPose)
            {
                thisDCA.GetComponent<Animator>().enabled = false;
                thisDCA.KeepAnimatorController = true;
                /*thisDCA.activeRace.data.FixupRotations = false;
                Transform rootTransform = thisDCA.umaData.skeleton.GetRootTransform();
                Transform globalTransform = thisDCA.umaData.skeleton.GetGlobalTransform();

                globalTransform.localRotation = Quaternion.identity;
                rootTransform.localRotation = Quaternion.identity; */
            }
            else
            {
                thisDCA.GetComponent<Animator>().enabled = wasAnimatorEnabled;
                thisDCA.KeepAnimatorController = wasKeepAnimator;
                /*thisDCA.activeRace.data.FixupRotations = wasRaceFixup;
                Transform rootTransform = thisDCA.umaData.skeleton.GetRootTransform();
                Transform globalTransform = thisDCA.umaData.skeleton.GetGlobalTransform();
                globalTransform.localRotation = wasGlobalRotation;
                rootTransform.localRotation = wasRootRotation;*/
            }
            thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
            if (LoadMeshModifiers)
            {
                thisDCA.umaData.ManualMeshModifiers = DoModifierSplit(true);
            }
            vertexEditorStage.RebuildMesh(forceTPose,buildCollisionMesh);
        }

        public void DoCharacterReset()
        {
            thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
            vertexEditorStage.RebuildMesh(false);
        }

        /*
        public void DoCharacterRebuildWithUpdates()
        {
            Dictionary<string, MeshModifier.Modifier> testModifiers = new Dictionary<string, MeshModifier.Modifier>();

            foreach (VertexAdjustment va in vertexEditorStage.GetVertexAdjustments())
            {
                string key = va.Name + ":" + va.slotName;
                if (!testModifiers.ContainsKey(key))
                {
                    MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                    newMod.adjustments = va.VertexAdjustmentCollection;
                    newMod.SlotName = va.slotName;
                    newMod.ModifierName = va.Name;
                    testModifiers.Add(key, newMod);
                }
                testModifiers[key].adjustments.Add(va);
            }

            List<MeshModifier.Modifier> NewMods = new List<MeshModifier.Modifier>();
            // convert dictionary to a list of modifiers
            foreach (KeyValuePair<string, MeshModifier.Modifier> kvp in testModifiers)
            {
                NewMods.Add(kvp.Value);
            }

            thisDCA.umaData.manualMeshModifiers = NewMods;
            vertexEditorStage.RebuildMesh(false);
        }

        public void DoCharacterRebuildWithCurrentBulkModifiers()
        {
            thisDCA.umaData.manualMeshModifiers = new List<MeshModifier.Modifier>();
            foreach(MeshModifier.Modifier mod in Modifiers)
            {
                SplitModifiersBySlot(thisDCA.umaData.manualMeshModifiers, mod);
            }
            vertexEditorStage.RebuildMesh(false);
        } */

#if UMA_BURSTCOMPILE
        [BurstCompile(CompileSynchronously = true)]
#endif
        public void SplitModifiersBySlot(List<MeshModifier.Modifier> target, MeshModifier.Modifier activeModifier)
        {
            if (target == null || activeModifier == null)
            {
                return;
            }

            if (activeModifier.keepAsIs)
            {
                // No need to split, just add.
                target.Add(activeModifier);
                return;
            }

            if (activeModifier.adjustments == null || activeModifier.adjustments.vertexAdjustments == null)
            {
                return;
            }

            foreach (VertexAdjustment va in activeModifier.adjustments.vertexAdjustments)
            {
                if (va == null || string.IsNullOrEmpty(va.slotName))
                {
                    continue;
                }

                string key = va.slotName;
                MeshModifier.Modifier newMod = null;
                foreach (MeshModifier.Modifier mod in target)
                {
                    if (mod == null || mod.keepAsIs)
                    {
                        continue;
                    }

                    if (mod.adjustments == null)
                    {
                        continue;
                    }

                    Type modType = null;
                    if (mod.TemplateAdjustment != null)
                    {
                        modType = mod.TemplateAdjustment.GetType();
                    }
                    else
                    {
                        modType = mod.adjustments.AdjustmentType;
                    }

                    if (mod.SlotName == key && modType == va.GetType())
                    {
                        bool sameDNA = mod.DNAName == activeModifier.DNAName;
                        bool sameScale = Mathf.Approximately(mod.Scale, activeModifier.Scale);
                        if (sameDNA && sameScale)
                        {
                            newMod = mod;
                            break;
                        }
                    }
                }
                if (newMod == null)
                {
                    newMod = new MeshModifier.Modifier();
                    newMod.keepAsIs = false;
                    newMod.SlotName = key;
                    newMod.ModifierName = activeModifier.ModifierName;
                    newMod.DNAName = activeModifier.DNAName;
                    newMod.Scale = activeModifier.Scale;
                    newMod.TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(va.GetType());
                    newMod.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(activeModifier.adjustments.GetType());
                    target.Add(newMod);
                }
                newMod.adjustments.Add(va);
            }
        }

        public void DoCharacterRebuildWithActiveBulkModifier(MeshModifier.Modifier activeModifier)
        {
            thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
            SplitModifiersBySlot(thisDCA.umaData.ManualMeshModifiers, activeModifier);
            vertexEditorStage.RebuildMesh(false);
        }

        private int ShowActiveAdjustment(int activeCount, VertexAdjustment va)
        {
            if (va.active)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.9f, 0.9f, 1f));
                GUILayout.Label("Editor Active", centeredLabel);
                SetActive(va, true);
                activeCount++;
            }
            else
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.3f, 0.3f, 0.4f));
            }

            HandleInteractiveUndoCapture(ref activeAdjustmentInteractiveUndoArmed, "Edit Vertex Adjustment");

            if (va.DoGUI())
            {
                if (RebuildOnChanges)
                {
                    DoCharacterRebuild();
                }
            }
            if (va.Gizmo != VertexAdjustmentGizmo.None)
            {
                if (va.active)
                {
                    if (GUILayout.Button("Stop Editing"))
                    {
                        SetActive(va, false);
                    }
                }
                else
                {

                    if (GUILayout.Button("Edit in scene"))
                    {
                        SetActive(va);
                    }
                }
            }
            else
            {
                GUILayout.Label("No gizmo for this adjustment");
            }
            GUIHelper.EndVerticalPadded(10);
            return activeCount;
        }

        private void deActivateCurrentSelection()
        {
            VertexEditorStage.VertexSelection vs = vertexEditorStage.GetSelectedVertex();
            foreach (VertexAdjustment va in vertexEditorStage.GetVertexAdjustments())
            {
                if (va == null || vs == null || vs.slot == null)
                {
                    continue;
                }
                if (va.slotName == vs.slot.slotName && va.vertexIndex == vs.vertexIndexOnSlot)
                {
                    va.active = false;
                }
            }
        }


 
        public string FindNameForModifier(string typeName)
        {
            int maxNumber = 1;
            foreach (MeshModifier.Modifier mod in Modifiers)
            {
                if (mod.TemplateAdjustment.Name == typeName)
                {
                    string name = mod.ModifierName;
                    if (name.StartsWith(typeName))
                    {
                        string number = name.Substring(typeName.Length);
                        number = number.Replace("(", "");
                        number = number.Replace(")", "");

                        int num = 0;
                        if (int.TryParse(number, out num))
                        {
                            if (num >= maxNumber)
                            {
                                maxNumber = num+1;
                            }
                        }
                    }
                }
            }
            return $"{typeName} ({maxNumber})";
        }

        private void AddActiveVertexesToCollection(MeshModifier.Modifier meshModifier)
        {
            var SelectedVertexes = vertexEditorStage.GetActiveSelectedVertexes();

            foreach (var se in SelectedVertexes)
            {
                VertexAdjustment va = CreateVertexAdjustment(se, meshModifier.adjustments);
                meshModifier.adjustments.Add(va);
            }
        }
        private void AddAllVertexesToCollection(MeshModifier.Modifier meshModifier)
        {
            vertexEditorStage.SelectAll();
            var SelectedVertexes = vertexEditorStage.GetVertexSelections();

            foreach (var se in SelectedVertexes)
            {
                VertexAdjustment va = CreateVertexAdjustment(se, meshModifier.adjustments);
                meshModifier.adjustments.Add(va);
            }
        }


        private Vector2 ModifierScrollPos = Vector2.zero;

        private void OpenModifierInspector(int modifierIndex)
        {
            ModifierInspectWindow.ShowWindow(this, modifierIndex);
        }

        private void DrawMeshModifiers()
        {
            EditorGUILayout.LabelField("Mesh Modifiers", centeredLabel);
            EditorGUILayout.HelpBox("Recalculate normals to modifier will create a normal rotation modifier from the current normals and tangents to the recalculate normals and tangents. You should run this before doing any mesh modifications.", MessageType.Info);
            if (GUILayout.Button("Recalculate Normals to Reset Modifier"))
            {
                RegisterUndoSnapshot("Create Reset Normals Modifier", true);
                DoCharacterRebuild(true, false, false);
                // Get normals from "fresh" mesh.
                // Now recalculate normals and tangents.
                // then get the new normals.
                // go through the normals, and extract the rotation from/to.
                List<Vector3> oldNormals = new List<Vector3>(vertexEditorStage.BakedMesh.normals);
                vertexEditorStage.RecalculateNormals();
                List<Vector3> newNormals = new List<Vector3>(vertexEditorStage.BakedMesh.normals);



                var saveSelections = new List<VertexEditorStage.VertexSelection>();
                saveSelections.AddRange(vertexEditorStage.GetVertexSelections());
                MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                newMod.EditorInitialize(typeof(VertexNormalAdjustmentCollection));

                // newMod.EditorInitialize(typeof(VertexResetAdjustmentCollection));
                newMod.ModifierName = FindNameForModifier("Extracted recalculated normals");
                Modifiers.Add(newMod);
                newMod.adjustments = new VertexNormalAdjustmentCollection();
                // newMod.adjustments = new VertexResetAdjustmentCollection();
                AddAllVertexesToCollection(newMod);

                VertexNormalAdjustmentCollection theCollection = (VertexNormalAdjustmentCollection)newMod.adjustments;
                for (int i = 0; i < theCollection.vertexAdjustments.Count; i++)
                {
                    SlotData slot = thisDCA.umaData.umaRecipe.GetSlot(theCollection.vertexAdjustments[i].slotName);
                    if (slot == null)
                    {
                        continue;
                    }

                    if (!vertexEditorStage.TryGetVisibleBakedVertexIndex(slot, theCollection.vertexAdjustments[i].vertexIndex, out int vertPos))
                    {
                        continue;
                    }

                    VertexNormalAdjustment var = theCollection.vertexAdjustments[i] as VertexNormalAdjustment;
                    var.rotation = Quaternion.FromToRotation(oldNormals[vertPos], newNormals[vertPos]);
                    //var.initialNormal = normals[vertPos];
                    //var.initialTangent = tangents[vertPos];
                }
                currentModifierIndex = Modifiers.Count - 1;
                ModifierScrollPos.y = 100000;
                vertexEditorStage.SetVertexSelections(saveSelections);
                MarkEditorStateDirty(true);
            }

            EditorGUILayout.LabelField("Extract Bulk Modifier of Type:");
            int newSelectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames);
            if (newSelectedType != selectedType)
            {
                RegisterUndoSnapshot("Change Bulk Modifier Type");
                selectedType = newSelectedType;
                MarkEditorStateDirty();
            }

            int activeCount = vertexEditorStage.GetActiveSelectedVertexCount();
            if (activeCount == 0)
            {
                EditorGUILayout.LabelField("No vertexes selected", centeredLabel);
                EditorGUILayout.HelpBox("Please selectvertexes For Bulk Modifier", MessageType.Info);
            }
            else
            {
                if (GUILayout.Button("Add Collection for selected vertexes"))
                {
                    RegisterUndoSnapshot("Add Bulk Modifier Collection");
                    MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                    newMod.EditorInitialize(ModifierTypes[selectedType]);
                    newMod.ModifierName = FindNameForModifier(newMod.TemplateAdjustment.Name);
                    Modifiers.Add(newMod);
                    AddActiveVertexesToCollection(newMod);
                    currentModifierIndex = Modifiers.Count - 1;
                    ModifierScrollPos.y = 100000;
                    MarkEditorStateDirty();
                }
            }
            if (currentModifierIndex < 0 || currentModifierIndex >= Modifiers.Count)
            {
                return;
            }

            MeshModifier.Modifier currentModifier = Modifiers[currentModifierIndex];
            if (currentModifier == null)
            {
                return;
            }

            if (selectedType < 0 || selectedType >= ModifierTypes.Length)
            {
                return;
            }
            if (currentModifier.TemplateAdjustment == null)
            {
                currentModifier.EditorInitialize(ModifierTypes[selectedType]);
            }
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField($"{currentModifier.TemplateAdjustment.Name} {currentModifier.ModifierName}" , centeredLabel);
            EditorGUILayout.LabelField($"{currentModifier.adjustments.vertexAdjustments.Count} vertexes",centeredLabel);
            HandleInteractiveUndoCapture(ref bulkModifierInteractiveUndoArmed, "Edit Bulk Modifier");
            bool changed = currentModifier.TemplateAdjustment.DoGUI();
            // RebuildOnChanges = EditorGUILayout.Toggle("Rebuild on changes", RebuildOnChanges);

            if (changed)
            {
                // update all vertexes on the current modifier
                // with the new values.
                foreach (VertexAdjustment va in currentModifier.adjustments.vertexAdjustments)
                {
                    va.CopyFrom(currentModifier.TemplateAdjustment);
                }
                if (RebuildOnChanges)
                {
                    DoCharacterRebuildWithActiveBulkModifier(currentModifier);
                }
                MarkEditorStateDirty(true);
            }

          /*  if (GUILayout.Button("Rebuild with this adjustment"))
            {
                if (currentModifierIndex < Modifiers.Count)
                {
                    DoCharacterRebuildWithActiveBulkModifier(currentModifier);
                }
            }
            if (GUILayout.Button("Rebuild with all adjustments"))
            {
                DoCharacterRebuildWithCurrentBulkModifiers();
            } */

            GUIHelper.EndVerticalPadded(10);

            if (currentModifierIndex >= 0 && currentModifierIndex < Modifiers.Count)
            {
                // DrawCurrentModifier();
            }

            EditorGUILayout.LabelField("Mesh Modifier Collections", centeredLabel);

            ModifierScrollPos = EditorGUILayout.BeginScrollView(ModifierScrollPos);
            int deleteMe = -1;
            for (int i = 0; i < Modifiers.Count; i++)
            {
                MeshModifier.Modifier mod = Modifiers[i];
                GUILayout.BeginHorizontal();
                if (i == currentModifierIndex)
                {
                    GUILayout.Label("(edit)", GUILayout.Width(64));
                }
                else
                {
                    GUILayout.Label(" ", GUILayout.Width(64));
                }
                if (GUILayout.Button($"{mod.TemplateAdjustment.Name}:{mod.ModifierName}", EditorStyles.miniButtonMid, GUILayout.ExpandWidth(true)))
                {
                    RegisterUndoSnapshot("Select Bulk Modifier");
                    currentModifierIndex = i;
                    MarkEditorStateDirty();
                    Repaint();
                }
                if (GUILayout.Button("Inspect", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    OpenModifierInspector(i);
                }
                if(GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    RegisterUndoSnapshot("Remove Bulk Modifier");
                    deleteMe = i;
                }
                GUILayout.EndHorizontal();
                /*
                if (i != currentModifierIndex)
                {
                    //GUIHelper.BeginVerticalPadded(10, new Color(0.7f, 0.8f, 1f));
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Edit", GUILayout.Width(32)))
                    {
                        currentModifierIndex = i;
                    }
                    string type = mod.adjustments.GetType().Name;
                    EditorGUILayout.LabelField($"{i} - {mod.ModifierName}");
                    if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        deleteMe = i;
                    }
                    GUILayout.EndHorizontal();
                    // GUIHelper.EndVerticalPadded(10);
                }
                else
                {
                    DrawCurrentModifier(false);
                } */
            }
            EditorGUILayout.EndScrollView();
            if (deleteMe >= 0)
            {
                Modifiers.RemoveAt(deleteMe);
                if (currentModifierIndex >= Modifiers.Count)
                {
                    currentModifierIndex = Modifiers.Count - 1;
                }
                MarkEditorStateDirty();
            }
        }

        private void DrawCurrentModifier()
        {
            MeshModifier.Modifier mod = Modifiers[currentModifierIndex];
            GUIHelper.BeginVerticalPadded(10, backColor);
            GUILayout.Label("Type: "+ mod.TemplateAdjustment.Name, centeredLabel);
            GUILayout.Label("Vertex count: " + mod.adjustments.Count());
            mod.ModifierName = EditorGUILayout.TextField("Modifier Name", mod.ModifierName);
            mod.DNAName = EditorGUILayout.TextField("DNA Name", mod.DNAName);
            mod.Scale = EditorGUILayout.FloatField("Scale", mod.Scale);
             
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Vertexes"))
            {
                vertexEditorStage.SelectVertexes(mod.adjustments);
            }
            if (GUILayout.Button("Edit active"))
            {
                editingCurrent = true;
            }
            if (GUILayout.Button("Delete"))
            {
                Modifiers.RemoveAt(currentModifierIndex);
                if (currentModifierIndex >= Modifiers.Count)
                {
                    currentModifierIndex = Modifiers.Count - 1;
                }
            }
            GUILayout.EndHorizontal();
            // TODO: 
            // Add a way to add and remove vertex adjustments
            // Add a way to edit the vertex adjustments (display the active ones). 
            // Add a button to select all the adjusted vertexes on the character.
            // Add the ability to edit/filter to the "active" vertexes.
            // The currently selected vertex to edit should be selected and flashing or something on the character.
            GUIHelper.EndVerticalPadded(10);
        }

        private class ModifierInspectWindow : EditorWindow
        {
            private MeshModifierEditor owner;
            private int modifierIndex = -1;
            private Vector2 vertexScroll;
            private List<SlotData> addSlots = new List<SlotData>();
            private string[] addSlotNames = new string[0];
            private int addSlotIndex = 0;
            private int addVertexIndex = 0;

            public static void ShowWindow(MeshModifierEditor owner, int modifierIndex)
            {
                ModifierInspectWindow wnd = GetWindow<ModifierInspectWindow>(true, "Modifier Inspector", true);
                wnd.minSize = new Vector2(640, 420);
                wnd.Initialize(owner, modifierIndex);
                wnd.Show();
            }

            private void Initialize(MeshModifierEditor modifierEditor, int index)
            {
                owner = modifierEditor;
                modifierIndex = index;
                RefreshSlotCache();
            }

            private MeshModifier.Modifier GetModifier()
            {
                if (owner == null || owner.Modifiers == null)
                {
                    return null;
                }
                if (modifierIndex < 0 || modifierIndex >= owner.Modifiers.Count)
                {
                    return null;
                }
                return owner.Modifiers[modifierIndex];
            }

            private void RefreshSlotCache()
            {
                addSlots = new List<SlotData>();
                if (owner == null || owner.thisDCA == null || owner.thisDCA.umaData == null || owner.thisDCA.umaData.umaRecipe == null || owner.thisDCA.umaData.umaRecipe.slotDataList == null)
                {
                    addSlotNames = new string[0];
                    addSlotIndex = 0;
                    return;
                }

                foreach (var slot in owner.thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (slot != null && slot.asset != null && slot.asset.meshData != null)
                    {
                        addSlots.Add(slot);
                    }
                }

                addSlotNames = new string[addSlots.Count];
                for (int i = 0; i < addSlots.Count; i++)
                {
                    addSlotNames[i] = addSlots[i].slotName;
                }

                if (addSlotIndex >= addSlotNames.Length)
                {
                    addSlotIndex = Mathf.Max(0, addSlotNames.Length - 1);
                }
            }

            private bool ContainsVertex(MeshModifier.Modifier modifier, string slotName, int vertexIndex)
            {
                for (int i = 0; i < modifier.adjustments.vertexAdjustments.Count; i++)
                {
                    VertexAdjustment existing = modifier.adjustments.vertexAdjustments[i];
                    if (existing.slotName == slotName && existing.vertexIndex == vertexIndex)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool TryAddVertex(MeshModifier.Modifier modifier, SlotData slot, int vertexIndex)
            {
                if (modifier == null || modifier.adjustments == null || slot == null || slot.asset == null || slot.asset.meshData == null)
                {
                    return false;
                }

                if (vertexIndex < 0 || vertexIndex >= slot.asset.meshData.vertexCount)
                {
                    return false;
                }

                if (ContainsVertex(modifier, slot.slotName, vertexIndex))
                {
                    return false;
                }

                VertexAdjustment newAdjustment = modifier.adjustments.Create();
                newAdjustment.slotName = slot.slotName;
                newAdjustment.vertexIndex = vertexIndex;
                newAdjustment.active = false;
                newAdjustment.Init(slot.asset.meshData);

                if (modifier.TemplateAdjustment != null)
                {
                    newAdjustment.CopyFrom(modifier.TemplateAdjustment);
                }

                modifier.adjustments.Add(newAdjustment);
                return true;
            }

            private void OnGUI()
            {
                MeshModifier.Modifier modifier = GetModifier();
                if (modifier == null)
                {
                    EditorGUILayout.HelpBox("This modifier is no longer available.", MessageType.Info);
                    if (GUILayout.Button("Close"))
                    {
                        Close();
                    }
                    return;
                }

                EditorGUILayout.LabelField($"Modifier: {modifier.ModifierName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Type: {(modifier.TemplateAdjustment != null ? modifier.TemplateAdjustment.Name : "Unknown")}");
                EditorGUILayout.LabelField($"Slot: {modifier.SlotName}");
                EditorGUILayout.LabelField($"DNA: {modifier.DNAName}");
                EditorGUILayout.LabelField($"Scale: {modifier.Scale}");
                EditorGUILayout.LabelField($"Keep As Is: {modifier.keepAsIs}");
                EditorGUILayout.LabelField($"Vertex Count: {(modifier.adjustments != null ? modifier.adjustments.vertexAdjustments.Count : 0)}");

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Modifier Vertices"))
                {
                    owner.vertexEditorStage.SelectVertexes(modifier.adjustments);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Add Active Scene Selection"))
                {
                    owner.RegisterUndoSnapshot("Inspect Add Active Vertices");
                    int added = 0;
                    var selected = owner.vertexEditorStage.GetActiveSelectedVertexes();
                    for (int i = 0; i < selected.Count; i++)
                    {
                        if (TryAddVertex(modifier, selected[i].slot, selected[i].vertexIndexOnSlot))
                        {
                            added++;
                        }
                    }

                    if (added > 0)
                    {
                        owner.MarkEditorStateDirty(true);
                        if (owner.RebuildOnChanges)
                        {
                            owner.DoCharacterRebuild();
                        }
                        owner.Repaint();
                        Repaint();
                    }
                }
                GUILayout.EndHorizontal();

                RefreshSlotCache();
                GUILayout.Space(4);
                GUILayout.Label("Add Vertex", EditorStyles.boldLabel);
                if (addSlotNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("No valid slots found on this character.", MessageType.Warning);
                }
                else
                {
                    addSlotIndex = EditorGUILayout.Popup("Slot", addSlotIndex, addSlotNames);
                    addVertexIndex = EditorGUILayout.IntField("Vertex Index", addVertexIndex);
                    if (GUILayout.Button("Add Vertex"))
                    {
                        SlotData slot = addSlots[addSlotIndex];
                        owner.RegisterUndoSnapshot("Inspect Add Vertex");
                        if (TryAddVertex(modifier, slot, addVertexIndex))
                        {
                            owner.MarkEditorStateDirty(true);
                            if (owner.RebuildOnChanges)
                            {
                                owner.DoCharacterRebuild();
                            }
                            owner.Repaint();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Add Vertex", "Could not add vertex. Verify slot, vertex index range, and duplicate state.", "OK");
                        }
                    }
                }

                GUILayout.Space(8);
                GUILayout.Label("Vertices", EditorStyles.boldLabel);
                vertexScroll = EditorGUILayout.BeginScrollView(vertexScroll);
                int removeIndex = -1;
                if (modifier.adjustments != null)
                {
                    for (int i = 0; i < modifier.adjustments.vertexAdjustments.Count; i++)
                    {
                        VertexAdjustment va = modifier.adjustments.vertexAdjustments[i];
                        if (va == null)
                        {
                            continue;
                        }

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{i}: {va.slotName} / {va.vertexIndex}", GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            removeIndex = i;
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();

                if (removeIndex >= 0)
                {
                    owner.RegisterUndoSnapshot("Inspect Remove Vertex");
                    modifier.adjustments.vertexAdjustments.RemoveAt(removeIndex);
                    owner.MarkEditorStateDirty(true);
                    if (owner.RebuildOnChanges)
                    {
                        owner.DoCharacterRebuild();
                    }
                    owner.Repaint();
                }
            }
        }

        private void OnDestroy()
        {
            // if ModifierEditor is closed before the VertexEditorStage is closed.
            if (vertexEditorStage != null)
            {
               bool wasChanged = false;
                if (Modifiers != null)
                {
                    if (Modifiers.Count > 0)
                    {
                        wasChanged = true;
                    }
                    if (vertexEditorStage.Adjustments.Count > 0)
                    {
                        wasChanged = true;
                    }
                }
                if (wasChanged)
                {
                    if (EditorUtility.DisplayDialog("ModifierEditor Save Changes", "Do you want to save the changes you made to the modifiers?", "Yes", "No"))
                    {
                        SaveToAsset();
                    }
                } 
                vertexEditorStage.hasSaved = true;
                vertexEditorStage.CloseStage();
            }
        }
    }
}