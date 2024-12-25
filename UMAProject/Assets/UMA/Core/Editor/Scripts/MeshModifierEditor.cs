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
        public MeshModifier CurrentModifier = null;
        public Type[] ModifierTypes = new Type[0];
        public string[] ModifierTypeNames = new string[0];
        public int selectedType = 0;
        public VertexAdjustment templateAdjustment = null;
        public VertexAdjustmentCollection templateVertexAdjustmentCollection = null;

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
                CurrentModifier = modifier;
                Modifiers = modifier.Modifiers;
            }
            // vertexEditorStage = VertexEditorStage.ShowStage(DCA);
        }

        public void OnGUI()
        {
            if (thisDCA == null)
            {
                EditorGUILayout.LabelField("No DCA selected");
                return;
            }

            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

            EditorGUILayout.LabelField("Select Modifier Type");
            selectedType = EditorGUILayout.Popup(selectedType, ModifierTypeNames);

            if (templateVertexAdjustmentCollection == null || ModifierTypes[selectedType] != templateVertexAdjustmentCollection.GetType())
            {
                templateVertexAdjustmentCollection = (VertexAdjustmentCollection)Activator.CreateInstance(ModifierTypes[selectedType]);
            }

            EditorGUILayout.LabelField("Modifier");
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
            if (GUILayout.Button("Add Active Vertexes"))
            {
                if (CurrentModifier == null)
                {
                   // CurrentModifier = new MeshModifier();
                   // Modifiers.Add(CurrentModifier);
                }
               // CurrentModifier.Modifiers.Add(templateVertexAdjustmentCollection);
            }

            GUIHelper.EndVerticalPadded(10);
            EditorGUILayout.LabelField("Mesh Modifiers for " + thisDCA.name);

            if (CurrentModifier != null)
            {
                EditorGUILayout.LabelField("Current Modifier: " + CurrentModifier.name);
            }


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