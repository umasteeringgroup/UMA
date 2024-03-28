using UnityEngine;
using UnityEditor;
using UMA.Examples;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMASimpleLOD))]
    public class UMASimpleLODEditor : Editor
    {

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if(Application.isPlaying)
            {
                EditorGUILayout.LabelField("Current LOD", ((UMASimpleLOD)target).CurrentLOD.ToString());
            }

            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
