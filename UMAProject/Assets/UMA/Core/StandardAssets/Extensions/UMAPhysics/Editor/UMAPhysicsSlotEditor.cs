using UnityEngine;
using UnityEditor;

namespace UMA.Dynamics.Editors
{
	[CustomEditor(typeof(UMAPhysicsSlotDefinition))]
	public class UMAPhysicsSlotEditor : Editor 
	{
		SerializedProperty ragdollLayer;
		SerializedProperty playerLayer;

		void OnEnable()
		{
			ragdollLayer = serializedObject.FindProperty("ragdollLayer");
			playerLayer = serializedObject.FindProperty("playerLayer");
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			DrawDefaultInspector();

			EditorGUILayout.HelpBox ("Sets layer 8 and 9 to Ragdoll and Player. If your code uses different layers do not use this defaults button", MessageType.Info);
			if (GUILayout.Button ("Add Default Layers")) 
			{
				UMAPhysicsAvatarEditor.AddDefaultLayers (ragdollLayer, playerLayer);
			}
			EditorGUILayout.HelpBox ("The Ragdoll layer needs it's collision matrix layers set to collide with only itself. Set this in Edit->Project Settings->Physics->Layer Collision Matrix", MessageType.Info);

			ragdollLayer.intValue = EditorGUILayout.LayerField ("Ragdoll Layer", ragdollLayer.intValue);
			playerLayer.intValue = EditorGUILayout.LayerField ("Player Layer", playerLayer.intValue);

			serializedObject.ApplyModifiedProperties();
		}
	}
}
