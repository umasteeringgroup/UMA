using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(SharedColorTable))]
	public class SharedColorTableEditor : Editor 
	{
        public override void OnInspectorGUI()
        {
            SharedColorTable sct = target as SharedColorTable;
            if (sct == null)
            {
                return;
            }
            EditorGUILayout.LabelField("Shared Color Table", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This is a shared color table. It is used to share color tables between multiple DynamicCharacterAvatars. It is not intended to be used directly.", MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Color Table", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This is the color table that will be shared between multiple DynamicCharacterAvatars. It is not intended to be used directly.", MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("colors"), true);
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}

