using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomPropertyDrawer(typeof(ColorOverridePieceProperty))]
	//[CustomEditor(typeof(ColorOverridePieceProperty))]
	public class ColorOverridePropertyDrawer : BasePropertyDrawer<ColorOverrideProperty>
	{
		protected override void OnPublicGUI(ColorOverrideProperty value)
		{
			value.color = EditorGUILayout.ColorField("Default", value.color);
			
			GUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Lock Channels");
			value.overrideR = GUILayout.Toggle(value.overrideR, "Red");
			value.overrideG = GUILayout.Toggle(value.overrideG, "Green");
			value.overrideB = GUILayout.Toggle(value.overrideB, "Blue");
			value.overrideA = GUILayout.Toggle(value.overrideA, "Alpha");
			GUILayout.EndHorizontal();
		}
		
		protected override void OnConstantGUI(ColorOverrideProperty value)
		{
			value.color = EditorGUILayout.ColorField("Value", value.color);
		}
		
		protected override void OnRequiredGUI(ColorOverrideProperty value)
		{
			value.color = EditorGUILayout.ColorField("Locked", value.color);
			GUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Lock Channels");
			value.overrideR = GUILayout.Toggle(value.overrideR, "Red");
			value.overrideG = GUILayout.Toggle(value.overrideG, "Green");
			value.overrideB = GUILayout.Toggle(value.overrideB, "Blue");
			value.overrideA = GUILayout.Toggle(value.overrideA, "Alpha");
			GUILayout.EndHorizontal();
		}
	}	
}