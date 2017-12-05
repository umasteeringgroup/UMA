using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomEditor(typeof(LocationCondition))]
	public class LocationConditionInspector : Editor
	{
		public override void OnInspectorGUI()
		{
			var locationCondition = target as LocationCondition;
			locationCondition.Location = EditorGUILayout.ObjectField("Location", locationCondition.Location, typeof(UMALocation), false) as UMALocation;
			locationCondition.Condition = (LocationCondition.ConditionRule) EditorGUILayout.EnumPopup("Condition", locationCondition.Condition);
		}
	}
}