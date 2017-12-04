using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomEditor(typeof(ColorPieceProperty))]
	public class ColorPropertyDrawer : Editor 
	{
		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();
			var value = (target as ColorPieceProperty).value;
			value.color = EditorGUILayout.ColorField("Color", value.color);
			if (EditorGUI.EndChangeCheck())
			{
				EditorUtility.SetDirty(target);
			}
		}
	}	
}