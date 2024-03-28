using UnityEngine;
using UnityEditor;

namespace UMA.Dynamics.Editors
{
	[CustomEditor(typeof(UMAPhysicsAvatar))]
	public class UMAPhysicsAvatarEditor : Editor 
	{
		SerializedProperty ragdollLayer;
		SerializedProperty playerLayer;
		SerializedProperty onRagdollStarted;
		SerializedProperty onRagdollEnded;

		void OnEnable()
		{
			ragdollLayer = serializedObject.FindProperty("ragdollLayer");
			playerLayer = serializedObject.FindProperty("playerLayer");
			onRagdollStarted = serializedObject.FindProperty("onRagdollStarted");
			onRagdollEnded = serializedObject.FindProperty("onRagdollEnded");
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			UMAPhysicsAvatar avatar = (UMAPhysicsAvatar)target;	
			avatar.ragdolled = EditorGUILayout.Toggle(new GUIContent("Ragdolled", "Toggle to turn on/off the Ragdoll"), avatar.ragdolled);
			//DrawDefaultInspector();
			DrawPropertiesExcluding(serializedObject, new string[]{ "ragdollLayer", "playerLayer", "onRagdollStarted", "onRagdollEnded" });

			GUILayout.Space(10);
			EditorGUILayout.HelpBox("Sets layer 8 and 9 to Ragdoll and Player. If your code uses different layers do not use this defaults button", MessageType.Info);
			if (GUILayout.Button("Add Default Layers"))
			{
				AddDefaultLayers( ragdollLayer, playerLayer);
			}
			EditorGUILayout.HelpBox("The Ragdoll layer needs it's collision matrix layers set to collide with only itself. Set this in Edit->Project Settings->Physics->Layer Collision Matrix", MessageType.Info);
			ragdollLayer.intValue = EditorGUILayout.LayerField("Ragdoll Layer", ragdollLayer.intValue);
			playerLayer.intValue = EditorGUILayout.LayerField("Player Layer", playerLayer.intValue);

			GUILayout.Space(10);
			EditorGUILayout.PropertyField(onRagdollStarted);
			EditorGUILayout.PropertyField(onRagdollEnded);

			serializedObject.ApplyModifiedProperties();
		}

		static public void AddDefaultLayers(SerializedProperty ragdollLayer, SerializedProperty playerLayer)
		{
			ragdollLayer.intValue = UMAUtils.CreateLayer("Ragdoll");
			playerLayer.intValue = UMAUtils.CreateLayer("Player");

			for (int i = 8; i < 32; i++)
			{
				if (i != ragdollLayer.intValue)
                {
                    Physics.IgnoreLayerCollision(ragdollLayer.intValue, i, true);
                }
            }

			Physics.IgnoreLayerCollision(ragdollLayer.intValue, ragdollLayer.intValue, false);
		}
	}
}