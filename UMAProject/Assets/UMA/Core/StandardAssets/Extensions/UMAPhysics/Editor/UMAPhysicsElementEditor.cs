using UnityEditor;

namespace UMA.Dynamics.Editors
{
	[CustomEditor(typeof(UMAPhysicsElement))]
	public class UMAPhysicsElementEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			//DrawDefaultInspector();
			serializedObject.Update();
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("isRoot"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("boneName"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("mass"));

			Show( serializedObject.FindProperty("colliders"));

			EditorGUILayout.PropertyField (serializedObject.FindProperty ("parentBone"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("axis"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("swingAxis"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("lowTwistLimit"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("highTwistLimit"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("swing1Limit"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("swing2Limit"));
			EditorGUILayout.PropertyField (serializedObject.FindProperty ("enablePreprocessing"));

			serializedObject.ApplyModifiedProperties ();
		}

		private void Show(SerializedProperty list)
		{
			EditorGUILayout.PropertyField (list); //List Name
			EditorGUI.indentLevel += 1;

			if (list.isExpanded) 
			{
				EditorGUILayout.PropertyField (list.FindPropertyRelative ("Array.size")); //List size

				for (int i = 0; i < list.arraySize; i++) 
				{
					EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i));
					EditorGUI.indentLevel += 1;

					if (list.GetArrayElementAtIndex (i).isExpanded) 
					{
						EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i).FindPropertyRelative ("colliderType"));
						int type = list.GetArrayElementAtIndex (i).FindPropertyRelative ("colliderType").enumValueIndex;
						if( type == 0 )	EditorGUILayout.HelpBox("Box Colliders can not be used to affect cloth.", MessageType.Warning );
						EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i).FindPropertyRelative ("colliderCentre"));

						if (type == 0) {
							//Box Collider only
							EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i).FindPropertyRelative ("boxDimensions"));
						}

						if (type == 1) {
							//Sphere Collider only
							EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i).FindPropertyRelative ("sphereRadius"));
						}

						if (type == 2) {
							//Capsule Collider only
							EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i).FindPropertyRelative ("capsuleRadius"));
							EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i).FindPropertyRelative ("capsuleHeight"));
							EditorGUILayout.PropertyField (list.GetArrayElementAtIndex (i).FindPropertyRelative ("capsuleAlignment"));
						}
					}
					EditorGUI.indentLevel -= 1;
				}
			}

			EditorGUI.indentLevel -= 1;
		}
	}
}