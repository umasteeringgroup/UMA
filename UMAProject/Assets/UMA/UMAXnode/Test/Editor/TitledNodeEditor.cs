using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA.XNode;
using UMA.XNodeEditor;


[CustomNodeEditor(typeof(TitledNode))]
public class TitledNodeEditor : UMA.XNodeEditor.NodeEditor
{
    public override void OnHeaderGUI()
    {
        GUILayout.Label(((ITitleSupplier)target).GetTitle(), UMA.XNodeEditor.NodeEditorResources.styles.nodeHeader, GUILayout.Height(30));
    }
    public override void OnBodyGUI()
    {
        base.OnBodyGUI();

        if (target is IContentSupplier)
        {
            ((IContentSupplier)target).OnGUI();
        }
    }
}
