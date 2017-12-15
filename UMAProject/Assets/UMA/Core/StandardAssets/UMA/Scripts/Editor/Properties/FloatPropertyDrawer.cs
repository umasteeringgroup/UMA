using UnityEngine;
using UnityEditor;

namespace UMA
{
	//[CustomEditor(typeof(FloatPieceProperty))]
	[CustomPropertyDrawer(typeof(FloatPieceProperty))]
	public class FloatPropertyDrawer : BasePropertyDrawer<FloatProperty>
	{
		protected override void OnPublicGUI(FloatProperty value)
		{
			value.value = EditorGUILayout.Slider("Default", value.value, 0f, 1f);
		}
		
		protected override void OnConstantGUI(FloatProperty value)
		{
			value.value = EditorGUILayout.Slider("Value", value.value, 0f, 1f);
		}
	}	
}