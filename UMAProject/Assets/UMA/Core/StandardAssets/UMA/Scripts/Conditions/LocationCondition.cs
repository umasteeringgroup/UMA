#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	public class LocationCondition : BaseCondition 
	{
		public UMALocation Location;
		public ConditionRule Condition;
		public enum ConditionRule
		{
			Empty,
			Set,
			Active
		}
		
		public override bool ConditionMet()
		{
			return true;
		}

#if UNITY_EDITOR
		public override float GetInspectorHeight()
		{
			return CalculateElementHeight(2);
		}

		public override void DrawInspectorProperties(InspectorRect rect, bool isActive, bool isFocused)
		{
			Location = EditorGUI.ObjectField(rect.GetLineRect(), "Location", Location, typeof(UMALocation), false) as UMALocation;
			Condition = (LocationCondition.ConditionRule)EditorGUI.EnumPopup(rect.GetLineRect(), "Condition", Condition);
		}
#endif
	}
}