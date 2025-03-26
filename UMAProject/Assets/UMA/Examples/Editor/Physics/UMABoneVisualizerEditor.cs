using UnityEngine;
using UnityEditor;

namespace UMA
{
    [CustomEditor(typeof(UMABoneVisualizer))]
    public class Example : Editor
    {
        static GUIContent Warning = new GUIContent("This is a helper component and should be removed before your final build. It has no runtime functionality.");
        public override void OnInspectorGUI()
        {
            UMABoneVisualizer targetPlayer = (UMABoneVisualizer)target;
            Rect labelRect = GUILayoutUtility.GetRect(Warning, "box");
            GUI.Box(labelRect, Warning);
            DrawDefaultInspector();
        }
    }
}