using UnityEngine;
using UnityEditor;

namespace UMA
{
	[UMACustomPropertyDrawer(typeof(LocationCondition))]
	[CustomPropertyDrawer(typeof(LocationCondition), true)]
	public class LocationConditionInspector : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return InspectableAsset.CalculateElementHeight(2);
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			var rect = new InspectorRect(position);
			Debug.Log(position);
			EditorGUI.LabelField(rect.GetLineRect(), "property.type", property.type);
			EditorGUI.LabelField(rect.GetLineRect(), "property.propertyType", property.propertyType.ToString());

			//Location = EditorGUI.ObjectField(rect.GetLineRect(), "Location", Location, typeof(UMALocation), false) as UMALocation;
			//Condition = (LocationCondition.ConditionRule)EditorGUI.EnumPopup(rect.GetLineRect(), "Condition", Condition);
		}
	}
}