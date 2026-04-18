using UnityEditor;
using UnityEngine;

namespace UMA
{
    [CustomEditor(typeof(DualQuaternionSkinnerUMA))]
    public class DualQuaternionSkinnerUMAEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var skin = (DualQuaternionSkinnerUMA)target;
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable DQS"))
            {
                skin.EnableDQS();
            }
            if (GUILayout.Button("Disable DQS"))
            {
                skin.DisableDQS();
            }
            if (GUILayout.Button("Bake To MeshFilter"))
            {
                skin.BakeToMeshFilter();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
