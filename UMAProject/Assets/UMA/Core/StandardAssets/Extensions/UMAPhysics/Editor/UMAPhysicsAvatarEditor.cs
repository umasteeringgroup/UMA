using UnityEngine;
using UnityEditor;

namespace UMA.Dynamics.Editors
{
	[CustomEditor(typeof(UMAPhysicsAvatar))]
	public class UMAPhysicsAvatarEditor : Editor 
	{
		public override void OnInspectorGUI()
		{
			UMAPhysicsAvatar avatar = (UMAPhysicsAvatar)target;	
			avatar.ragdolled = EditorGUILayout.Toggle(new GUIContent("Ragdolled", "Toggle to turn on/off the Ragdoll"), avatar.ragdolled);
			DrawDefaultInspector();
		}
	}
}