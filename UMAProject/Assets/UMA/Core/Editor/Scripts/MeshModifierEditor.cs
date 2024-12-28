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
        }

        public void OnGUI()
        {
            if (thisDCA == null)
            {
                EditorGUILayout.LabelField("No DCA selected");
                return;
            }

            if (GUILayout.Button("Add Mesh Modifier Collection"))
            {
                MeshModifier.Modifier newMod = new MeshModifier.Modifier();
                Modifiers.Add(newMod);
                currentModifierIndex = Modifiers.Count -1;
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
                templateVertexAdjustmentCollection.DoGUI(templateAdjustment);
            }
            if (GUILayout.Button("Add Active Vertexes to selected set"))
            {

            }

            GUIHelper.EndVerticalPadded(10);
            EditorGUILayout.LabelField("Mesh Modifier Collections", centeredLabel);

            for (int i=0;i< Modifiers.Count; i++)
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
                    DrawCurrentModifier();
                }
            }
        }

        private void DrawCurrentModifier()
        {
            MeshModifier.Modifier mod = Modifiers[currentModifierIndex];
            GUIHelper.BeginVerticalPadded(10, backColor);
            mod.ModifierName = EditorGUILayout.TextField("Modifier Name", mod.ModifierName);
            mod.DNAName = EditorGUILayout.TextField("DNA Name", mod.DNAName);
            mod.Scale = EditorGUILayout.FloatField("Scale", mod.Scale);
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