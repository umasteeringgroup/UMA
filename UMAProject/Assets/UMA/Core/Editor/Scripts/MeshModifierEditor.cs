using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UMA;
using UMA.CharacterSystem;
using PlasticGui.Help;
using UMA.Editors;
using System;
using System.Net;
using System.Data.SqlTypes;

namespace UMA
{
    public class MeshModifierEditor : EditorWindow
    {
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
        public VertexAdjustment templateAdjustment = null;
        public VertexAdjustmentCollection templateVertexAdjustmentCollection = null;
        public GUIStyle centeredLabel = new GUIStyle();
        public Color backColor = Color.cyan;
        public bool editingCurrent = false;
        public GUIStyle selectedButton = new GUIStyle(EditorStyles.miniButton);
        public GUIStyle unselectedButton = new GUIStyle(EditorStyles.miniButton);
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
            selectedButton.normal.textColor = Color.white;
            selectedButton.normal.background = new Texture2D(1, 1);
            selectedButton.normal.background.SetPixel(0, 0, Color.blue);
            selectedButton.normal.background.Apply();
            unselectedButton.normal.textColor = Color.black;
            unselectedButton.normal.background = new Texture2D(1, 1);
            unselectedButton.normal.background.SetPixel(0, 0, Color.white);
            unselectedButton.normal.background.Apply();
        }
        

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

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Current Vertex", VertexModeStyle))
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
                DrawVertexAdjustments();
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
        private bool showFiltered = true;

        private void DrawVertexAdjustments()
        {
            int activeCount = 0;

            if (vertexEditorStage.CurrentSelected < 0)
            {
                //Debug.Log("No vertexes selected. CurrentSelect <= 0");
                EditorGUILayout.LabelField("No Vertexes Selected", centeredLabel);
                EditorGUILayout.HelpBox("Please click one of the selected vertexes in the scene view to edit it. The vertex can be active or inactive.", MessageType.Info);
                return;
            }
            
            VertexEditorStage.VertexSelection selectedVertex = vertexEditorStage.GetSelectedVertex();
            if (selectedVertex == null)
            {
                EditorGUILayout.LabelField("No Vertexes Selected", centeredLabel);
                EditorGUILayout.HelpBox("Please click one of the selected vertexes in the scene view to edit it. The vertex can be active or inactive.", MessageType.Info);
                return;
            }
            if (selectedVertex.suppressed)
            {
                EditorGUILayout.LabelField("Vertex is suppressed", centeredLabel);
                EditorGUILayout.HelpBox("This vertex is suppressed and cannot be edited. A vertex is suppressed when the slot it is on is hidden.", MessageType.Info);
                return;
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
                VertexAdjustment va = templateVertexAdjustmentCollection.Create();
                va.vertexIndex = selectedVertex.vertexIndexOnSlot;
                va.slotName = selectedVertex.slot.slotName;
                va.active = true;
                va.Init(selectedVertex.slot.asset.meshData);
                int newSize = vertexEditorStage.GetAdjustments().Count + 1;
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
            if (GUILayout.Button("Rebuild with current adjustments"))
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


                Modifiers.Clear();
                // convert dictionary to a list of modifiers
                foreach (KeyValuePair<string, MeshModifier.Modifier> kvp in testModifiers)
                {
                    Modifiers.Add(kvp.Value);
                }

                thisDCA.umaData.manualMeshModifiers = Modifiers;
                vertexEditorStage.RebuildMesh(false);
            }
            GUIHelper.EndVerticalPadded(10);

            


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
                    SetActive(va, !va.active);
                }
                delme = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();

                if (delme)
                {
                    RemoveMe = va;
                }

                //if (FoldOuts[pos])
                //{
                //    activeCount = ShowActiveAdjustment(activeCount, va);
                //}
                pos++;
            }
            if (activeCount == 0)
            {
                vertexEditorStage.SetActive(null);
            }
            if (RemoveMe != null)
            {
                vertexEditorStage.RemoveVertexAdjustment(RemoveMe);
            }
            EditorGUILayout.EndScrollView();
        }

        private int ShowActiveAdjustment(int activeCount, VertexAdjustment va)
        {
            if (va.active)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.9f, 0.9f, 1f));
                GUILayout.Label("Editor Active", centeredLabel);
                SetActive(va, va.Gizmo != VertexAdjustmentGizmo.None);
                activeCount++;
            }
            else
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.3f, 0.3f, 0.4f));
            }


            va.DoGUI();
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
                if (va.slotName == vs.slot.slotName && va.vertexIndex == vs.vertexIndexOnSlot)
                {
                    va.active = false;
                }
            }
        }

        private void DrawMeshModifiers()
        {
            if (editingCurrent)
            {
                EditorGUILayout.LabelField("Editing Mesh Modifier Collection", centeredLabel);
                if (GUILayout.Button("Return to Mesh Modifiers"))
                {
                    editingCurrent = false;
                    return;
                }
                DrawCurrentModifier(true);
                //DrawFilteredVertexAdjustments();
                return;
            }

            if (GUILayout.Button("Add Mesh Modifier Collection"))
            {
                MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                Modifiers.Add(newMod);
                currentModifierIndex = Modifiers.Count - 1;
            }
            if (Modifiers.Count == 0)
            {
                EditorGUILayout.LabelField("No Mesh Modifiers collections");
                return;
            }
            else
            {
                EditorGUILayout.LabelField("Mesh Modifiers for " + thisDCA.name);
            }




            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            EditorGUILayout.LabelField("Add Vertexe Modifications", centeredLabel);

            EditorGUILayout.LabelField("Select Modifier Type");
            selectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames);

            if (templateVertexAdjustmentCollection == null || ModifierTypes[selectedType] != templateVertexAdjustmentCollection.GetType() && selectedType < ModifierTypes.Length)
            {
                templateVertexAdjustmentCollection = (VertexAdjustmentCollection)Activator.CreateInstance(ModifierTypes[selectedType]);
            }

            EditorGUILayout.LabelField("Set Modifier values: ");
            if (templateAdjustment == null && templateVertexAdjustmentCollection != null)
            {
                templateAdjustment = (VertexAdjustment)Activator.CreateInstance(templateVertexAdjustmentCollection.AdjustmentType);
            }
            if (templateAdjustment.GetType() != templateVertexAdjustmentCollection.AdjustmentType)
            {
                templateAdjustment = (VertexAdjustment)Activator.CreateInstance(templateVertexAdjustmentCollection.AdjustmentType);
            }
            if (templateVertexAdjustmentCollection != null && templateAdjustment != null)
            {
                templateAdjustment.DoGUI();
            }
            if (GUILayout.Button("Add Active Vertexes to selected set"))
            {

            }

            GUIHelper.EndVerticalPadded(10);
            EditorGUILayout.LabelField("Mesh Modifier Collections", centeredLabel);

            for (int i = 0; i < Modifiers.Count; i++)
            {
                MeshModifier.Modifier mod = Modifiers[i];
                if (i != currentModifierIndex)
                {
                    GUIHelper.BeginVerticalPadded(10, new Color(0.7f, 0.8f, 1f));
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Edit", GUILayout.Width(32)))
                    {
                        currentModifierIndex = i;
                    }
                    EditorGUILayout.LabelField($"Collection {i} - {mod.ModifierName}");
                    GUILayout.EndHorizontal();
                    GUIHelper.EndVerticalPadded(10);
                }
                else
                {
                    DrawCurrentModifier(false);
                }
            }
        }

        private void DrawCurrentModifier(bool vertexMode)
        {
            MeshModifier.Modifier mod = Modifiers[currentModifierIndex];
            GUIHelper.BeginVerticalPadded(10, backColor);
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
            if (vertexEditorStage != null)
            {
                vertexEditorStage.CloseStage();
            }
        }
    }
}