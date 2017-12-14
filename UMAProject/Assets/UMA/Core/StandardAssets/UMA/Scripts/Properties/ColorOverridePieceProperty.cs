using System;
using UnityEngine;

namespace UMA
{
	using UnityEditor;
	public class ColorOverridePieceProperty : BasePieceProperty<ColorOverrideProperty>
	{
		
		#if UNITY_EDITOR
		public override float GetInspectorHeight()
		{
			return (propertyType == PropertyType.Constant) ? 19 : 38;
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
				
				var lineRect = new InspectorRect(EditorGUI.PrefixLabel(rect.GetLineRect(), new GUIContent("Lock Channels")));
				value.overrideR = GUI.Toggle(lineRect.GetColumnRect(0,4), value.overrideR, "Red");
				value.overrideG = GUI.Toggle(lineRect.GetColumnRect(1,4), value.overrideG, "Green");
				value.overrideB = GUI.Toggle(lineRect.GetColumnRect(2,4), value.overrideB, "Blue");
				value.overrideA = GUI.Toggle(lineRect.GetColumnRect(3,4), value.overrideA, "Alpha");
				
				break;
			case PropertyType.Required:
				value.color = EditorGUI.ColorField(rect.GetLineRect(), "Locked", value.color);
				
				var reqLineRect = new InspectorRect(EditorGUI.PrefixLabel(rect.GetLineRect(), new GUIContent("Lock Channels")));
				value.overrideR = GUI.Toggle(reqLineRect.GetColumnRect(0,4), value.overrideR, "Red");
				value.overrideG = GUI.Toggle(reqLineRect.GetColumnRect(1,4), value.overrideG, "Green");
				value.overrideB = GUI.Toggle(reqLineRect.GetColumnRect(2,4), value.overrideB, "Blue");
				value.overrideA = GUI.Toggle(reqLineRect.GetColumnRect(3,4), value.overrideA, "Alpha");
				
				break;
			default:
				break;
			}
		}
		#endif
	}
}