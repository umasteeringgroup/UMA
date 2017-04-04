using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomPropertyDrawer(typeof(OverlayColorData),true)]
	public class OverlayColorDataPropertyDrawer : PropertyDrawer
	{
		bool showAdvanced;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);
			var name = property.FindPropertyRelative("name");
			var mask = property.FindPropertyRelative("channelMask");
			var additive = property.FindPropertyRelative("channelAdditiveMask");
			EditorGUILayout.BeginHorizontal();
			name.isExpanded = EditorGUILayout.Foldout(name.isExpanded, label);
			if (!name.isExpanded)
				name.stringValue = EditorGUILayout.TextField(new GUIContent(""), name.stringValue);
			EditorGUILayout.EndHorizontal();
			if (name.isExpanded)
			{
				EditorGUILayout.PropertyField(property.FindPropertyRelative("name"));

				showAdvanced = EditorGUILayout.Toggle("Show Extended Ranges", showAdvanced);
				//var mask = property.FindPropertyRelative("channelMask");
				//var additive = property.FindPropertyRelative("channelAdditiveMask");
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
			}
			else
			{
				EditorGUILayout.PropertyField(mask.GetArrayElementAtIndex(0),new GUIContent("BaseColor"));
				if(additive.arraySize >= 3)
					EditorGUILayout.PropertyField(additive.GetArrayElementAtIndex(2),new GUIContent("Metallic/Gloss", "Color is metallicness (Black is not metallic), Alpha is glossiness (Black is not glossy)"));
				else
				{
					//color didn't have a metallic gloss channel so show button to add one?
				}
			}
			EditorGUILayout.Space();
			EditorGUI.EndProperty();
		}
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			/*var name = property.FindPropertyRelative("name");
			if (!name.isExpanded)
			{
				return (EditorGUIUtility.singleLineHeight * 3f) - 2f;
			}*/
			return -2f;
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
