using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	public class ColorPieceProperty : BasePieceProperty<ColorProperty>
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
				value.color = EditorGUI.ColorField(rect.GetLineRect(), "Value", value.color);
				break;
			case PropertyType.Public:
				value.color = EditorGUI.ColorField(rect.GetLineRect(), "Default", value.color);
				break;
			default:
				break;
			}
		}
#endif
	}
}