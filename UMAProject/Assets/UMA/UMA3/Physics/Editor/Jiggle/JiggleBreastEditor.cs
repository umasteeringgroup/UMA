using UnityEngine;
using UnityEditor;
using UMA.Examples;

namespace UMA
{

	[CustomEditor(typeof(UMA_JiggleBreasts))]
	public class JiggleBreastEditor : Editor
	{

		public override void OnInspectorGUI()
		{
			var myScript = target as UMA_JiggleBreasts;
			GUILayout.Label("");
			GUILayout.Label("Breasts:");
			myScript._breastStiffness = EditorGUILayout.FloatField("Breast Stiffness (0-1):", Mathf.Clamp(myScript._breastStiffness, 0, 1));
			myScript._breastMass = EditorGUILayout.FloatField("Breast Mass (0-1 recommended):", myScript._breastMass);
			myScript._breastDamping = EditorGUILayout.FloatField("Breast damping (0-1):", Mathf.Clamp(myScript._breastDamping, 0, 1));
			myScript._breastGravity = EditorGUILayout.FloatField("Breast gravity (0-1 recommended):", myScript._breastGravity);
			myScript._breastInertia = EditorGUILayout.FloatField("Breast inertia (0-1):", Mathf.Clamp(myScript._breastInertia, 0, 1));
			myScript._breastMaxJiggleDistance = EditorGUILayout.FloatField("Breast max jiggle distance:", Mathf.Max(myScript._breastMaxJiggleDistance, 0.001f));
			myScript._breastTargetDistance = EditorGUILayout.FloatField("Breast target distance:", Mathf.Max(myScript._breastTargetDistance, 0.001f));
			myScript._breastPositionWeight = EditorGUILayout.FloatField("Breast position weight:", Mathf.Max(myScript._breastPositionWeight, 0f));
			myScript._breastRotationWeight = EditorGUILayout.FloatField("Breast rotation weight (0-1):", Mathf.Clamp(myScript._breastRotationWeight, 0, 1));
			myScript._breastSquashAndStretch = GUILayout.Toggle(myScript._breastSquashAndStretch, "Do you want breast stretching?");
			if (myScript._breastSquashAndStretch)
			{
				myScript._breastFrontStretch = EditorGUILayout.FloatField("Breast Front Stretch (0-1):", Mathf.Clamp(myScript._breastFrontStretch, 0, 1));
				myScript._breastSideStretch = EditorGUILayout.FloatField("Breast Side Stretch (0-1):", Mathf.Clamp(myScript._breastSideStretch, 0, 1));
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
