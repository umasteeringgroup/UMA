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
		readonly GUIContent MoveUpIcon = GetMoveButtonContent("ArrowNavigationUp", "\u25B2", "Move color up");
		readonly GUIContent MoveDownIcon = GetMoveButtonContent("ArrowNavigationDown", "\u25BC", "Move color down");

		private static GUIContent GetMoveButtonContent(string iconName, string fallbackText, string tooltip)
		{
			/*
			GUIContent iconContent = EditorGUIUtility.IconContent(iconName);
			if (iconContent != null && iconContent.image != null)
			{
				iconContent.tooltip = tooltip;
				return iconContent;
			} */

			return new GUIContent(fallbackText, tooltip);
		}


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
			var selected = property.FindPropertyRelative("isSelected");
			var showSelected = property.FindPropertyRelative("showSelected");
			var moveUp = property.FindPropertyRelative("moveUpThis");
			var moveDown = property.FindPropertyRelative("moveDownThis");


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
			if (showSelected.boolValue == true)
			{
				selected.boolValue = EditorGUILayout.Toggle(selected.boolValue, GUILayout.Width(20), GUILayout.ExpandWidth(false));
				EditorGUILayout.Space(10, false);
            }
			
		    label.text = name.stringValue;
            name.isExpanded = EditorGUILayout.Foldout(name.isExpanded, label);

           if (!name.isExpanded)
            {
                int arrayIndex = GetArrayIndex(property.propertyPath);
				int arraySize = GetArraySize(property.propertyPath, property.serializedObject);
				bool showDisplayColor = property.FindPropertyRelative("showDisplayColor").boolValue;
				if (showDisplayColor)
				{
					SerializedProperty displayColorProp = property.FindPropertyRelative("displayColor");
					Color c = displayColorProp.colorValue;
					Color b = EditorGUILayout.ColorField(c, GUILayout.Width(200));
                    if (b != c)
                    {
                        displayColorProp.colorValue = b;
                        displayColorProp.serializedObject.ApplyModifiedProperties();
                    }
                }
				else
				{
					if (mask.arraySize > 0)
					{
						SerializedProperty colProp = mask.GetArrayElementAtIndex(0);

						Color c = colProp.colorValue;
						Color b = EditorGUILayout.ColorField(c, GUILayout.Width(200));
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
				}
               using (new EditorGUI.DisabledScope(arrayIndex <= 0))
				{
					if (GUILayout.Button(MoveUpIcon, EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(18)))
					{
						moveUp.boolValue = true;
					}
				}
				using (new EditorGUI.DisabledScope(arrayIndex < 0 || arrayIndex >= arraySize - 1))
				{
					if (GUILayout.Button(MoveDownIcon, EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(18)))
					{
						moveDown.boolValue = true;
					}
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
				EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.LabelField("Overlay Color Data", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(property.FindPropertyRelative("name"));
				EditorGUILayout.PropertyField(property.FindPropertyRelative("isBaseColor"));
				EditorGUILayout.PropertyField(property.FindPropertyRelative("showDisplayColor"));
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


				GUILayout.BeginHorizontal();
                GUILayout.Space(10);
                colorFoldout.boolValue = EditorGUILayout.Foldout(colorFoldout.boolValue, "Colors");
				GUILayout.EndHorizontal();

                if (colorFoldout.boolValue)
				{
                    GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
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
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField($"Tex {i}", EditorStyles.miniLabel,GUILayout.Width(50));
                            Modulate.text = "";
							EditorGUILayout.LabelField("Mult", EditorStyles.miniLabel, GUILayout.Width(40));
                            EditorGUILayout.PropertyField(mask.GetArrayElementAtIndex(i),Modulate,GUILayout.MinWidth(50));
							Additive.text = "";
							GUILayout.FlexibleSpace();
							EditorGUILayout.LabelField("Add", EditorStyles.miniLabel, GUILayout.Width(40));
                            EditorGUILayout.PropertyField(additive.GetArrayElementAtIndex(i), Additive, GUILayout.MinWidth(50));
							EditorGUILayout.EndHorizontal();
                        }
						//GUILayout.Space(5);
					}
					if (GUILayout.Button("Reset all colors to defaults"))
					{
						if (ocd != null)
						{
							for (int i = 0; i < mask.arraySize; i++)
							{
                                var channelMask = mask.GetArrayElementAtIndex(i);
                                channelMask.colorValue = new Color(1, 1, 1, 1);

                                var AdditiveMask = additive.GetArrayElementAtIndex(i);
								AdditiveMask.colorValue = new Color(0, 0, 0, 0);
                            }

                            if (dca != null)
							{
								EditorUtility.SetDirty(dca);
								AssetDatabase.SaveAssets();
							}
						}
                    }
                    GUIHelper.EndVerticalPadded(3);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Space(10);
                propertiesFoldout.boolValue = EditorGUILayout.Foldout(propertiesFoldout.boolValue, "Color Parameters");
				GUILayout.EndHorizontal();
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
				EditorGUILayout.EndVertical();
            }
            GUILayout.Box(GUIContent.none, GUILayout.ExpandWidth(true), GUILayout.Height(1));
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

		private int GetArrayIndex(string propertyPath)
		{
			int lastOpenBracket = propertyPath.LastIndexOf('[');
			int lastCloseBracket = propertyPath.LastIndexOf(']');
			if (lastOpenBracket < 0 || lastCloseBracket <= lastOpenBracket)
			{
				return -1;
			}

			string indexText = propertyPath.Substring(lastOpenBracket + 1, lastCloseBracket - lastOpenBracket - 1);
			return int.TryParse(indexText, out int index) ? index : -1;
		}

		private int GetArraySize(string propertyPath, SerializedObject serializedObject)
		{
			int arrayMarkerIndex = propertyPath.IndexOf(".Array.data[");
			if (arrayMarkerIndex < 0)
			{
				return 0;
			}

			string arrayPath = propertyPath.Substring(0, arrayMarkerIndex);
			SerializedProperty arrayProperty = serializedObject.FindProperty(arrayPath);
			return arrayProperty != null ? arrayProperty.arraySize : 0;
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
