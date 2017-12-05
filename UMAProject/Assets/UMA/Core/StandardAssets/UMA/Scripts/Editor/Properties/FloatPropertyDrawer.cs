using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomEditor(typeof(FloatPieceProperty))]
	public class FloatPropertyDrawer : Editor 
	{
		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();
			var value = (target as FloatPieceProperty).value;
			value.value = EditorGUILayout.Slider("Value", value.value, 0f, 1f);
			if (EditorGUI.EndChangeCheck())
			{
				EditorUtility.SetDirty(target);
			}
		}
	}	
}