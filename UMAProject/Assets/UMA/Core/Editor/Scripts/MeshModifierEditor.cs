using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UMA;
using UMA.CharacterSystem;
using UMA.Editors;
using System;

namespace UMA
{
    public class MeshModifierEditor : EditorWindow
    {
        public bool RebuildOnChanges = false;
        public List<MeshModifier.Modifier> Modifiers = new List<MeshModifier.Modifier>();

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
        public enum EditorMode { MeshModifiers, VertexAdjustments }
        public EditorMode editorMode = EditorMode.VertexAdjustments;

        public void Setup(DynamicCharacterAvatar DCA, VertexEditorStage vstage, MeshModifier modifier)
        {
            thisDCA = DCA;
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

            if (editorMode == EditorMode.MeshModifiers)
            {
                MeshModifierModeStyle = selectedButton;
            }
            else
            {
                VertexModeStyle = selectedButton;
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
            vertexEditorStage.editorMode = editorMode;
            GUILayout.EndHorizontal();

            if (editorMode == EditorMode.MeshModifiers)
            {
                DrawMeshModifiers();
            }
            else
            {
                DrawAdHocAdjustments();
            }
        }

        public void SaveToAsset()
        {
            string Path = EditorUtility.SaveFilePanelInProject("Save MeshModifier", "MeshModifier", "asset", "Save current MeshModifier to project");
            if (Path != "")
            {
                string BaseName = System.IO.Path.GetFileNameWithoutExtension(Path);
                MeshModifier meshModifier = CustomAssetUtility.CreateAsset<MeshModifier>(Path, false, BaseName, false);
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
        public void DoCharacterRebuild()
        {
            thisDCA.umaData.manualMeshModifiers = new List<MeshModifier.Modifier>();
            thisDCA.umaData.manualMeshModifiers = DoModifierSplit(true);
            vertexEditorStage.RebuildMesh(false);
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
            foreach (VertexAdjustment va in activeModifier.adjustments.vertexAdjustments)
            {
                string key = va.slotName;
                MeshModifier.Modifier newMod = null;
                foreach (MeshModifier.Modifier mod in target)
                {
                    if (mod.SlotName == key && mod.TemplateAdjustment.GetType() == va.GetType())
                    {
                        newMod = mod;
                        break;
                    }
                }
                if (newMod == null)
                {
                    newMod = new MeshModifier.Modifier();
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

        private Vector2 ModifierScrollPos = Vector2.zero;
        private void DrawMeshModifiers()
        {
            EditorGUILayout.LabelField("Mesh Modifiers", centeredLabel);

            EditorGUILayout.LabelField("Select Modifier Type");
            selectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames);

            int activeCount = vertexEditorStage.GetActiveSelectedVertexCount();
            if (activeCount == 0)
            {
                EditorGUILayout.LabelField("No vertexes selected", centeredLabel);
                EditorGUILayout.HelpBox("Please select some vertexes to add a modifier to.", MessageType.Info);
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
                return;
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