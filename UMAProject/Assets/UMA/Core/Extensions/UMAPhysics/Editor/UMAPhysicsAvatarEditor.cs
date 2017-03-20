using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.PhysicsAvatar;

[CustomEditor(typeof(UMAPhysicsAvatar))]
public class UMAPhysicsAvatarEditor : Editor 
{
	public override void OnInspectorGUI()
	{
		UMAPhysicsAvatar avatar = (UMAPhysicsAvatar)target;		
		avatar.ragdolled = EditorGUILayout.Toggle("Ragdolled", avatar.ragdolled);
		DrawDefaultInspector();
	}
}
