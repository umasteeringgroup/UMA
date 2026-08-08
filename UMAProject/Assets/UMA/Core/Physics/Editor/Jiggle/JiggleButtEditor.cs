#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UMA.Examples;

namespace UMA
{
	[CustomEditor(typeof(UMA_JiggleButt))]
	public class JiggleButtEditor : Editor
	{

		public override void OnInspectorGUI()
		{
			var myScript = target as UMA_JiggleButt;
			GUILayout.Label("");
			GUILayout.Label("Buttocks:");
			myScript._buttStiffness = EditorGUILayout.FloatField("Buttock Stiffness (0-1):", Mathf.Clamp(myScript._buttStiffness, 0, 1));
			myScript._buttMass = EditorGUILayout.FloatField("Buttock Mass (0-1 recommended):", myScript._buttMass);
			myScript._buttDamping = EditorGUILayout.FloatField("Buttock damping (0-1):", Mathf.Clamp(myScript._buttDamping, 0, 1));
			myScript._buttGravity = EditorGUILayout.FloatField("Buttock gravity (0-1 recommended):", myScript._buttGravity);
			myScript._buttInertia = EditorGUILayout.FloatField("Buttock inertia (0-1):", Mathf.Clamp(myScript._buttInertia, 0, 1));
			myScript._buttMaxJiggleDistance = EditorGUILayout.FloatField("Buttock max jiggle distance:", Mathf.Max(myScript._buttMaxJiggleDistance, 0.001f));
			myScript._buttTargetDistance = EditorGUILayout.FloatField("Buttock target distance:", Mathf.Max(myScript._buttTargetDistance, 0.001f));
			myScript._buttPositionWeight = EditorGUILayout.FloatField("Buttock position weight:", Mathf.Max(myScript._buttPositionWeight, 0f));
			myScript._buttRotationWeight = EditorGUILayout.FloatField("Buttock rotation weight (0-1):", Mathf.Clamp(myScript._buttRotationWeight, 0, 1));
			myScript._buttSquashAndStretch = GUILayout.Toggle(myScript._buttSquashAndStretch, "Do you want buttock stretching?");
			if (myScript._buttSquashAndStretch)
			{
				myScript._buttFrontStretch = EditorGUILayout.FloatField("Buttock Rear Stretch (0-1):", Mathf.Clamp(myScript._buttFrontStretch, 0, 1));
				myScript._buttSideStretch = EditorGUILayout.FloatField("Buttock Side Stretch (0-1):", Mathf.Clamp(myScript._buttSideStretch, 0, 1));
			}
			if (GUI.changed)
			{
				for (int i = 0; i < myScript._jigglers.Count; i++)
				{
					myScript.UpdateJiggleBone(myScript._jigglers[i]);
				}
				EditorUtility.SetDirty(target);
			}
		}

	}
}
#endif