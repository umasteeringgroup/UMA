using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;

namespace UMA
{

    // MeshModifier is a ScriptableObject that contains lists of VertexAdjustments.
    // Note: You will be asked to name the MeshModifier when you create it.
    // The slot name will be added to the MeshModifier So if you name it "HideBelly", and you edit the torso, you will get a slot named "HideBelly_Torso".
    // The editor will allow you to choose a different slot. If it does, another MeshModifier will be created with the new slot name.

    [CustomEditor(typeof(MeshModifier))]
    public class MeshModifierInspector : Editor
    {
        // A vertex group must be selected.
        // You add and remove vertexes from the selected group. 
        // Should we allow multiple slots to be selected?
        // in one MeshModifier?
        public int selectedGroupIndex = 0;


        // Start is called before the first frame update
        void Start()
        {

        }

        public override void OnInspectorGUI()
        {
            MeshModifier meshModifier = (MeshModifier)target;
            /*
            EditorGUILayout.LabelField("Slot Name", meshModifier.SlotName);
            EditorGUILayout.LabelField("DNA Name", meshModifier.DNAName);
            EditorGUILayout.LabelField("Scale", meshModifier.Scale.ToString());

            EditorGUILayout.LabelField("Normal Adjustments", meshModifier.normalAdjustments.ToString());
            EditorGUILayout.LabelField("Color Adjustments", meshModifier.colorAdjustments.ToString());
            EditorGUILayout.LabelField("Delta Adjustments", meshModifier.deltaAdjustments.ToString());
            EditorGUILayout.LabelField("Scale Adjustments", meshModifier.scaleAdjustments.ToString());
            EditorGUILayout.LabelField("UV Adjustments", meshModifier.uvAdjustments.ToString());
            EditorGUILayout.LabelField("Blendshape Adjustments", meshModifier.blendshapeAdjustments.ToString());
            EditorGUILayout.LabelField("User Adjustments", meshModifier.userAdjustments.ToString());*/
        }
    }
}