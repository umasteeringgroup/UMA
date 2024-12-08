using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UMA;
using UMA.CharacterSystem;

public class MeshModifierEditor : EditorWindow
{
   /* [MenuItem("Examples/My Editor Window")]
    public static void ShowExample()
    {
        MeshModifierEditor wnd = GetWindow<MeshModifierEditor>();
        wnd.Setup();
        wnd.titleContent = new GUIContent("Mesh Modifiers");
    }*/

    public static void GetOrCreateWindow(DynamicCharacterAvatar DCA)
    {
        MeshModifierEditor wnd = GetWindow<MeshModifierEditor>();
        wnd.Setup(DCA);
        wnd.titleContent = new GUIContent("Mesh Modifiers");
    }

    public DynamicCharacterAvatar thisDCA;
    public Dictionary<string,MeshModifier> SlotNameToModifiers = new Dictionary<string, MeshModifier>();
    public bool ShowVisibleSlots = false;
    public bool ShowOptions = false;

    public void Setup(DynamicCharacterAvatar DCA)
    {
        SlotNameToModifiers.Clear();
    }

    public void OnGUI()
    {
        if (thisDCA == null)
        {
            EditorGUILayout.LabelField("No DCA selected");
            return;
        }

        EditorGUILayout.LabelField("Mesh Modifiers for " + thisDCA.name);

        foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
        {
            MeshModifier modifier = null;
            if (!SlotNameToModifiers.TryGetValue(slot.slotName, out modifier))
            {
                modifier = ScriptableObject.CreateInstance<MeshModifier>();
                modifier.SlotName = slot.slotName;
                SlotNameToModifiers[slot.slotName] = modifier;
            }

            EditorGUILayout.LabelField("Slot Name", modifier.SlotName);
            EditorGUILayout.LabelField("DNA Name", modifier.DNAName);
            EditorGUILayout.LabelField("Scale", modifier.Scale.ToString());

            EditorGUILayout.LabelField("Normal Adjustments", modifier.normalAdjustments.ToString());
            EditorGUILayout.LabelField("Color Adjustments", modifier.colorAdjustments.ToString());
            EditorGUILayout.LabelField("Delta Adjustments", modifier.deltaAdjustments.ToString());
            EditorGUILayout.LabelField("Scale Adjustments", modifier.scaleAdjustments.ToString());
            EditorGUILayout.LabelField("UV Adjustments", modifier.uvAdjustments.ToString());
            EditorGUILayout.LabelField("Blendshape Adjustments", modifier.blendshapeAdjustments.ToString());
            EditorGUILayout.LabelField("User Adjustments", modifier.userAdjustments.ToString());
        }
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // VisualElements objects can contain other VisualElement following a tree hierarchy
        Label label = new Label("Hello World!");
        root.Add(label);

        // Create button
        Button button = new Button();
        button.name = "button";
        button.text = "Button";
        root.Add(button);

        // Create toggle
        Toggle toggle = new Toggle();
        toggle.name = "toggle";
        toggle.label = "Toggle";
        root.Add(toggle);
    }
}