using UnityEditor;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAGenerator))]
    public class UMAGeneratorEditor : UMAGeneratorBuiltinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space();
           /// EditorGUILayout.LabelField("Available Mesh Combiners", EditorStyles.boldLabel);
        }
    }
}

