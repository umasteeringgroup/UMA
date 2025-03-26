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
                Modifiers = modifier.Modifiers;
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


            IncludeAdHocAdjustments = GUILayout.Toggle(IncludeAdHocAdjustments,"Include Ad-Hoc adjustments");
            IncludeBulkModifiers = GUILayout.Toggle(IncludeBulkModifiers, "Include Bulk Modifiers");
            IncludeActiveOnlyBulk = GUILayout.Toggle(IncludeActiveOnlyBulk, "Only Active Bulk Modifier");
            RebuildOnChanges = GUILayout.Toggle(RebuildOnChanges, "Rebuild on changes");

            if (GUILayout.Button("Rebuild Now"))
            {
                DoCharacterRebuild();
            }
            if (GUILayout.Button("Rebuild to TPose"))
            {
                DoCharacterRebuild(true);
            }

            if (GUILayout.Button("Reset Build"))
            {
                DoCharacterReset();
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
                vertexEditorStage.RecalculateNormals();
            }

            if (IncludeBulkModifiers == false)
            {
                IncludeActiveOnlyBulk = false;
            }

            GUIHelper.EndVerticalPadded(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ad-hoc Adjustments", VertexModeStyle))
            {
                editorMode = EditorMode.VertexAdjustments;
            }
            if (GUILayout.Button("Bulk Add Active", MeshModifierModeStyle))
            {
                editorMode = EditorMode.MeshModifiers;
                deActivateCurrentSelection();
                vertexEditorStage.SetActive(null);
            }
            if (GUILayout.Button("Extract Blendshapes", BlendshapeStyle))
            {
                editorMode = EditorMode.Blendshapes;
                deActivateCurrentSelection();
                vertexEditorStage.SetActive(null);
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
        public void SetActive(VertexAdjustment va, bool activeState = true)
        {
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
            
            currentBlendshape = EditorGUILayout.Popup("Select Blendshape",currentBlendshape, strBlendShapes);
            currentDNA = EditorGUILayout.Popup("Select DNA", currentDNA, dnaNames);

            GUIHelper.BeginVerticalPadded();
            GUILayout.Label("Select Slots to extract blendshapes",centeredLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                for (int i = 0; i < blendShapeSlotSelected.Count; i++)
                {
                    blendShapeSlotSelected[i] = true;
                }
            }
            if (GUILayout.Button("Clear Selection"))
            {
                for (int i = 0; i < blendShapeSlotSelected.Count; i++)
                {
                    blendShapeSlotSelected[i] = false;
                }
            }
            GUILayout.EndHorizontal();
            for (int i = 0; i < blendShapeSlots.Count; i++)
            {
                blendShapeSlotSelected[i] = EditorGUILayout.Toggle(blendShapeSlots[i], blendShapeSlotSelected[i]);
            }
            GUIHelper.EndVerticalPadded();
            if (GUILayout.Button("Extract Blendshapes"))
            {
                ExtractBlendshapes(strBlendShapes[currentBlendshape],dnaNames[currentDNA],blendShapeSlotSelected,blendShapeSlots);
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
                        Modifiers.Remove(mod);
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
                selectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames, GUILayout.Width(180));
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
                    VertexAdjustment va = CreateVertexAdjustment(selectedVertex, templateVertexAdjustmentCollection);
                    int newSize = vertexEditorStage.GetVertexAdjustments().Count + 1;
                    FoldOuts = ExpandList(FoldOuts, newSize);
                    SetExpanded(FoldOuts, newSize - 1);
                    vertexEditorStage.AddVertexAdjustment(va);
                    SetActive(va);
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
                    SetActive(va,true);
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
                vertexEditorStage.RemoveVertexAdjustment(RemoveMe);
                if (RebuildOnChanges)
                {
                    DoCharacterRebuild();
                    //DoCharacterRebuildWithUpdates();
                }
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
            return result; 
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
            thisDCA.umaData.manualMeshModifiers = new List<MeshModifier.Modifier>();
            if (LoadMeshModifiers)
            {
                thisDCA.umaData.manualMeshModifiers = DoModifierSplit(true);
            }
            vertexEditorStage.RebuildMesh(forceTPose,buildCollisionMesh);
        }

        public void DoCharacterReset()
        {
            thisDCA.umaData.manualMeshModifiers = new List<MeshModifier.Modifier>();
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
            if (activeModifier.keepAsIs)
            {
                // No need to split, just add.
                target.Add(activeModifier);
                return;
            }
            foreach (VertexAdjustment va in activeModifier.adjustments.vertexAdjustments)
            {
                string key = va.slotName;
                MeshModifier.Modifier newMod = null;
                foreach (MeshModifier.Modifier mod in target)
                {
                    if (mod.SlotName == key && mod.TemplateAdjustment.GetType() == va.GetType() && mod.keepAsIs == false)
                    {
                        newMod = mod;
                        break;
                    }
                }
                if (newMod == null)
                {
                    newMod = new MeshModifier.Modifier();
                    newMod.keepAsIs = false;
                    newMod.SlotName = key;
                    newMod.ModifierName = "Bulk Adjustment";
                    newMod.TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(va.GetType());
                    newMod.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(activeModifier.adjustments.GetType());
                    target.Add(newMod);
                }
                newMod.adjustments.Add(va);
            }
        }

        public void DoCharacterRebuildWithActiveBulkModifier(MeshModifier.Modifier activeModifier)
        {
            thisDCA.umaData.manualMeshModifiers = new List<MeshModifier.Modifier>();
            SplitModifiersBySlot(thisDCA.umaData.manualMeshModifiers, activeModifier);
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
        private void DrawMeshModifiers()
        {
            EditorGUILayout.LabelField("Mesh Modifiers", centeredLabel);
            EditorGUILayout.HelpBox("Recalculate normals to modifier will create a normal rotation modifier from the current normals and tangents to the recalculate normals and tangents. You should run this before doing any mesh modifications.", MessageType.Info);
            if (GUILayout.Button("Recalculate Normals to Reset Modifier"))
            {
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
                    int vertPos = theCollection.vertexAdjustments[i].vertexIndex + slot.vertexOffset;
                    VertexNormalAdjustment var = theCollection.vertexAdjustments[i] as VertexNormalAdjustment;
                    var.rotation = Quaternion.FromToRotation(oldNormals[vertPos], newNormals[vertPos]);
                    //var.initialNormal = normals[vertPos];
                    //var.initialTangent = tangents[vertPos];
                }
                currentModifierIndex = Modifiers.Count - 1;
                ModifierScrollPos.y = 100000;
                vertexEditorStage.SetVertexSelections(saveSelections);
            }

            EditorGUILayout.LabelField("Extract Bulk Modifier of Type:");
            selectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames);

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
                    MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                    newMod.EditorInitialize(ModifierTypes[selectedType]);
                    newMod.ModifierName = FindNameForModifier(newMod.TemplateAdjustment.Name);
                    Modifiers.Add(newMod);
                    AddActiveVertexesToCollection(newMod);
                    currentModifierIndex = Modifiers.Count - 1;
                    ModifierScrollPos.y = 100000;
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
                    currentModifierIndex = i;
                    Repaint();
                }
                if(GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
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