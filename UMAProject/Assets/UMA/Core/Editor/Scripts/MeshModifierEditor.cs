using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UMA;
using UMA.CharacterSystem;

public class MeshModifierEditor : EditorWindow
{
    public static MeshModifierEditor GetOrCreateWindow(DynamicCharacterAvatar DCA, VertexEditorStage vstage)
    {
        MeshModifierEditor wnd = GetWindow<MeshModifierEditor>(true, "Mesh Modifiers",true);
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
    public Dictionary<string,MeshModifier> SlotNameToModifiers = new Dictionary<string, MeshModifier>();
    public bool ShowVisibleSlots = false;
    public bool ShowOptions = false;
    public VertexEditorStage vertexEditorStage;
    public MeshModifier CurrentModifier = null;

    public void Setup(DynamicCharacterAvatar DCA, VertexEditorStage vstage, MeshModifier modifier)
    {
        thisDCA = DCA;
        SlotNameToModifiers.Clear();
        vertexEditorStage = vstage;
        if (modifier == null)
        {
            // create a new modifier?
        }
        else
        {
            CurrentModifier = modifier;
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