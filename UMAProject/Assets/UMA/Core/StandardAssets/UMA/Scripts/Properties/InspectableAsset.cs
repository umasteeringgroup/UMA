#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace UMA
{
	public abstract class InspectableAsset : ScriptableObject 
	{
#if UNITY_EDITOR
		public virtual float GetInspectorHeight()
		{
			return 0;
		}
	
		public virtual void DrawInspectorProperties(InspectorRect rect, bool isActive, bool isFocused)
		{
		}

		public static float CalculateElementHeight(int lines, float custom = 0)
		{
			custom += lines * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
			if (custom > 0)
				custom += EditorGUIUtility.standardVerticalSpacing;
			return custom;
		}
#endif
	}
}