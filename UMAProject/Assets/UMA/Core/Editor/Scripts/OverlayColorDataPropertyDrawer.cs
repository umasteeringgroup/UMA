using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    [CustomPropertyDrawer(typeof(OverlayColorData),true)]
	public class OverlayColorDataPropertyDrawer : PropertyDrawer
	{
		GUIContent Modulate = new GUIContent("Multiplier");
		GUIContent Additive = new GUIContent("Additive");
		GUIContent Channels = new GUIContent("Channel Count");


		public static object GetDeepPropertyValue(object src, string propName)
		{
			if (propName.Contains('.'))
			{
				string[] Split = propName.Split('.');
				string RemainingProperty = propName.Substring(propName.IndexOf('.') + 1);
				return GetDeepPropertyValue(src.GetType().GetProperty(Split[0]).GetValue(src, null), RemainingProperty);
			}
			else
            {
                return src.GetType().GetProperty(propName).GetValue(src, null);
            }
        }

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{			
			var name = property.FindPropertyRelative("name");
			var mask = property.FindPropertyRelative("channelMask");
			var additive = property.FindPropertyRelative("channelAdditiveMask");
			var propblock = property.FindPropertyRelative("propertyBlock");
			var displayColor = property.FindPropertyRelative("displayColor");
			var colorFoldout = property.FindPropertyRelative("colorsExpanded");
			var propertiesFoldout = property.FindPropertyRelative("propertiesExpanded");
			
			OverlayColorData ocd = null;
			DynamicCharacterAvatar dca = property.serializedObject.targetObject as DynamicCharacterAvatar;

            ocd = property.GetValue<OverlayColorData>();
			if (ocd == null && dca != null)
			{
				string Name = property.FindPropertyRelative("name").stringValue;
				foreach( OverlayColorData o in dca.characterColors._colors)
				{
					if (o.name == Name)
					{
						ocd = o;
					}
				}
			}

			EditorGUI.BeginProperty(position, label, property);

			EditorGUILayout.BeginHorizontal();
		    label.text = name.stringValue;
            name.isExpanded = EditorGUILayout.Foldout(name.isExpanded, label);
			if (!name.isExpanded)
            {
                if (mask.arraySize > 0)
                {
					SerializedProperty colProp = mask.GetArrayElementAtIndex(0);

					Color c = colProp.colorValue;
					Color b =EditorGUILayout.ColorField(c, GUILayout.Width(200));
					if (b != c)
                    {
                        colProp.colorValue = b;
						colProp.serializedObject.ApplyModifiedProperties();
                    }
                }
				else
                {
					EditorGUILayout.ColorField(Color.white, GUILayout.Width(120));
                }
                bool delete = GUILayout.Button("X", GUILayout.Width(20));
                if (delete)
                {
                    property.FindPropertyRelative("deleteThis").boolValue = true;
                }
            }

            EditorGUILayout.EndHorizontal();
			if (name.isExpanded)
			{
				EditorGUILayout.LabelField("Overlay Color Data", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(property.FindPropertyRelative("name"));
				EditorGUILayout.PropertyField(property.FindPropertyRelative("isBaseColor"));
				EditorGUILayout.PropertyField(displayColor);

				if (ocd != null)
				{
					int ChannelCount = EditorGUILayout.IntSlider(Channels, ocd.channelCount, 0, 16);
					if (ChannelCount != ocd.channelCount)
					{
						ocd.SetChannels(ChannelCount);
						if (dca != null)
						{
							EditorUtility.SetDirty(dca);
						}
					}
				}

				SerializedProperty showAdvancedProperty = property.FindPropertyRelative("showAdvanced");
				EditorGUILayout.PropertyField(showAdvancedProperty);
				//showAdvanced = EditorGUILayout.Toggle("Show Extended Ranges", showAdvanced);

				GUILayout.Space(5);


				colorFoldout.boolValue = EditorGUILayout.Foldout(colorFoldout.boolValue, "Colors");
				if (colorFoldout.boolValue)
				{
					GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
					for (int i = 0; i < mask.arraySize; i++)
					{
						if (showAdvancedProperty.boolValue)
						{
							var channelMask = mask.GetArrayElementAtIndex(i);
							var channelColor = ToVector4(channelMask.colorValue);
							var newchannelColor = EditorGUILayout.Vector4Field("Multiplier (" + i + ")", channelColor);
							if (channelColor != newchannelColor)
							{
								channelMask.colorValue = ToColor(newchannelColor);
							}

							var AdditiveMask = additive.GetArrayElementAtIndex(i);
							var AdditiveColor = ToVector4(AdditiveMask.colorValue);
							var newAdditiveColor = EditorGUILayout.Vector4Field("Additive (" + i + ")", AdditiveColor);
							if (newAdditiveColor != AdditiveColor)
							{
								AdditiveMask.colorValue = ToColor(newAdditiveColor);
							}
						}
						else
						{
							Modulate.text = "Multiplier (" + i + ")";
							EditorGUILayout.PropertyField(mask.GetArrayElementAtIndex(i), Modulate);
							Additive.text = "Additive (" + i + ")";
							EditorGUILayout.PropertyField(additive.GetArrayElementAtIndex(i), Additive);
						}
						GUILayout.Space(5);
					}
					GUIHelper.EndVerticalPadded(3);
				}


				propertiesFoldout.boolValue = EditorGUILayout.Foldout(propertiesFoldout.boolValue, "Color Parameters");
				if (propertiesFoldout.boolValue)
				{
					if (ocd != null)
					{
						if (ocd.PropertyBlock != null)
						{
							if (UMAMaterialPropertyBlockDrawer.OnGUI(ocd.PropertyBlock))
							{
								if (dca != null)
								{
									EditorUtility.SetDirty(dca);
									AssetDatabase.SaveAssets();
								}
							}
						}
						else
						{
							if (GUILayout.Button("Add Properties Block"))
							{
								ocd.PropertyBlock = new UMAMaterialPropertyBlock();
								EditorUtility.SetDirty(dca);
								AssetDatabase.SaveAssets();
								//property.serializedObject.Update();
							}
						}
					}
				}
			}
			property.serializedObject.ApplyModifiedProperties();
            EditorGUI.EndProperty();
		}
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
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
	public class PropertyDrawerUtility
	{
		public static OverlayColorData GetOverlayDataAsset(System.Reflection.FieldInfo fieldInfo, SerializedProperty property)
		{ 
			DynamicCharacterAvatar dca = property.serializedObject.targetObject as DynamicCharacterAvatar;
			return new OverlayColorData();

		}
	}
}
