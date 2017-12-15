using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomPropertyDrawer(typeof(ColorPieceProperty))]
	//[CustomEditor(typeof(ColorPieceProperty))]
	public class ColorPropertyDrawer : BasePropertyDrawer<ColorProperty>
	{
		protected override void OnPublicGUI(ColorProperty value)
		{
			value.color = EditorGUILayout.ColorField("Default", value.color);
		}
		
		protected override void OnConstantGUI(ColorProperty value)
		{
			value.color = EditorGUILayout.ColorField("Value", value.color);
		}
	}	
}