using UnityEditor;
using UMA.Timeline;

namespace UMA.Editors
{
    [CustomEditor(typeof(UmaColorTrack))]
    public class UmaColorTrackEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty timeStep = serializedObject.FindProperty("timeStep");

            EditorGUILayout.PropertyField(timeStep);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
