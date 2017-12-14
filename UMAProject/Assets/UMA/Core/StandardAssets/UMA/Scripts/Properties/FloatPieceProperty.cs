using System;
using UnityEngine;

namespace UMA
{
	using UnityEditor;
	public class FloatPieceProperty : BasePieceProperty<FloatProperty>
	{
		#if UNITY_EDITOR
		public override float GetInspectorHeight()
		{
			return (propertyType == PropertyType.Constant || propertyType == PropertyType.Public) ? 19 : 0;
		}
		
		public override void DrawInspectorProperties(InspectorRect rect, bool isActive, bool isFocused)
		{
			switch (propertyType)
			{
			case PropertyType.Constant:
				value.value = EditorGUI.Slider(rect.GetLineRect(), "Value", value.value, 0f, 1f);
				break;
			case PropertyType.Public:
				value.value = EditorGUI.Slider(rect.GetLineRect(), "Default", value.value, 0f, 1f);
				break;
			default:
				break;
			}
		}
		#endif
	}
}