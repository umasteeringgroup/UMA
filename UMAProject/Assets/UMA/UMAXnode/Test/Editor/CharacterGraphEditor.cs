using UMA.XNodeEditor;
using UnityEditor;
using UnityEngine;

namespace UMA.XNode
{
    [CustomNodeGraphEditor(typeof(CharacterGraph))]
    public class CharacterGraphEditor : NodeGraphEditor
    {
        public override void OnGUI()
        {
            float width = window.position.width;
            GUILayout.BeginArea(new Rect(0, 0, width, 32));
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Character Graph", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            base.OnGUI();
            
        }
    }
}
