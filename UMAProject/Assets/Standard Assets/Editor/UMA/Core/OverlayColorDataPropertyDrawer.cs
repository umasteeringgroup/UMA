using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UMA;

namespace UMAEditor
{
	[CustomPropertyDrawer(typeof(OverlayColorData))]
	public class OverlayColorDataPropertyDrawer : PropertyDrawer
	{
		bool showAdvanced;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUILayout.PropertyField(property.FindPropertyRelative("name"));

			showAdvanced = EditorGUILayout.Toggle("Show Extended Ranges", showAdvanced);
			var mask = property.FindPropertyRelative("channelMask");
			var additive = property.FindPropertyRelative("channelAdditiveMask");
			for (int i = 0; i < mask.arraySize; i++)
			{
				if (showAdvanced)
				{
					var channelMask = mask.GetArrayElementAtIndex(i);
					var channelColor = ToVector4(channelMask.colorValue);
					channelColor = EditorGUILayout.Vector4Field("Multiplier", channelColor);
					if (GUI.changed)
					{
						channelMask.colorValue = ToColor(channelColor);
					}
				}
				else
				{
					EditorGUILayout.PropertyField(mask.GetArrayElementAtIndex(i));
				}

				EditorGUILayout.PropertyField(additive.GetArrayElementAtIndex(i));
			}
			EditorGUILayout.Space();
		}

		private Color ToColor(Vector4 colorVector)
		{
			return new Color(colorVector.x, colorVector.y, colorVector.z, colorVector.w);
		}

		private Vector4 ToVector4(Color color)
		{
			return new Vector4(color.r, color.g, color.b, color.a);
		}
	}
}
