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

    public static MeshModifierEditor GetOrCreateWindow(DynamicCharacterAvatar DCA, VertexEditorStage vstage)
    {
        MeshModifierEditor wnd = GetWindow<MeshModifierEditor>(true, "Mesh Modifiers",true);
        wnd.Setup(DCA, vstage);
        wnd.titleContent = new GUIContent("Mesh Modifiers");
        return wnd;
    }

    public DynamicCharacterAvatar thisDCA;
    public Dictionary<string,MeshModifier> SlotNameToModifiers = new Dictionary<string, MeshModifier>();
    public bool ShowVisibleSlots = false;
    public bool ShowOptions = false;
    public VertexEditorStage vertexEditorStage;

    public void Setup(DynamicCharacterAvatar DCA, VertexEditorStage vstage)
    {
        SlotNameToModifiers.Clear();
        vertexEditorStage = vstage;
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

    private void OnDestroy()
    {
        if (vertexEditorStage != null)
        {
            vertexEditorStage.CloseStage();
        }
    }
}