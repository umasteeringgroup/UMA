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
            EditorGUILayout.LabelField("Available Mesh Combiners", EditorStyles.boldLabel);
            var generator = (UMAGenerator)target;
            if (generator.availableMeshCombiners != null)
            {
                foreach (var meshCombiner in generator.availableMeshCombiners)
                {
                    EditorGUILayout.ObjectField(meshCombiner, typeof(UMAMeshCombiner), false);
                }
            }
        }
    }
}

