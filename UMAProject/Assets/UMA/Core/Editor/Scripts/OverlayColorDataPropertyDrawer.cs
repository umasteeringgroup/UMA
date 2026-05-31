using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    [CustomPropertyDrawer(typeof(OverlayColorData),true)]
	public class OverlayColorDataPropertyDrawer : PropertyDrawer
	{
		public static bool displayColorFoldout = false;
		private const string SharedColorTableFoldoutLabel = "select from Shared Color Table";
		private const double SharedColorTableCacheSeconds = 2.0;
		private static bool sharedColorTableFoldout = false;
		private static bool sharedColorTableCacheInitialized = false;
		private static double nextSharedColorTableRefreshTime = 0.0;
		private static SharedColorTable[] cachedSharedColorTables = new SharedColorTable[0];
		private static GUIContent[] cachedSharedColorTableOptions = new GUIContent[0];
		private static readonly Dictionary<string, SharedColorTable> selectedSharedColorTablesByProperty = new Dictionary<string, SharedColorTable>();
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
				bool appliedSharedColor = DrawSharedColorTableSelector(property, ocd, dca);
				if (appliedSharedColor)
				{
					ocd = property.GetValue<OverlayColorData>();
					name = property.FindPropertyRelative("name");
					mask = property.FindPropertyRelative("channelMask");
					additive = property.FindPropertyRelative("channelAdditiveMask");
					displayColor = property.FindPropertyRelative("displayColor");
					colorFoldout = property.FindPropertyRelative("colorsExpanded");
					propertiesFoldout = property.FindPropertyRelative("propertiesExpanded");
				}
                EditorGUILayout.LabelField("Overlay Color Data", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(property.FindPropertyRelative("name"));
				EditorGUILayout.PropertyField(property.FindPropertyRelative("isBaseColor"));
				EditorGUILayout.PropertyField(property.FindPropertyRelative("showDisplayColor"));
				displayColorFoldout = EditorGUILayout.Foldout(displayColorFoldout, "Display Color");
				if (displayColorFoldout)	
				{
					EditorGUILayout.HelpBox("This color is used for display purposes in user editors and does not affect the actual colors used in the character. It can be useful to set this to the approximate color that will be shown after combining onto the layers.", MessageType.Info);
                	EditorGUILayout.PropertyField(displayColor);
				}
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

		private bool DrawSharedColorTableSelector(SerializedProperty property, OverlayColorData currentOverlayColorData, DynamicCharacterAvatar dca)
		{
			sharedColorTableFoldout = EditorGUILayout.Foldout(sharedColorTableFoldout, SharedColorTableFoldoutLabel, true);
			if (!sharedColorTableFoldout)
			{
				return false;
			}

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			RefreshSharedColorTableCacheIfNeeded(false);

			if (cachedSharedColorTables.Length == 0)
			{
				EditorGUILayout.HelpBox("No SharedColorTable assets were found in the project.", MessageType.Info);
				EditorGUILayout.EndVertical();
				return false;
			}

			string propertyKey = GetPropertyStateKey(property);
			selectedSharedColorTablesByProperty.TryGetValue(propertyKey, out SharedColorTable selectedTable);
			int selectedTableIndex = GetSharedColorTableIndex(selectedTable);
			if (selectedTableIndex < 0)
			{
				selectedTableIndex = 0;
				selectedSharedColorTablesByProperty[propertyKey] = cachedSharedColorTables[selectedTableIndex];
			}

			GUILayout.BeginHorizontal();
			EditorGUI.BeginChangeCheck();
			int newSelectedTableIndex = EditorGUILayout.Popup(new GUIContent("Shared Color Table"), selectedTableIndex, cachedSharedColorTableOptions);
			if (EditorGUI.EndChangeCheck())
			{
				selectedTableIndex = newSelectedTableIndex;
				selectedSharedColorTablesByProperty[propertyKey] = cachedSharedColorTables[selectedTableIndex];
			}
			if (GUILayout.Button("Inspect", EditorStyles.miniButton, GUILayout.Width(64)))
			{
				EditorApplication.delayCall += () => InspectorUtlity.InspectTarget(cachedSharedColorTables[selectedTableIndex]);
			}
			GUILayout.EndHorizontal();

			selectedTable = cachedSharedColorTables[selectedTableIndex];
			if (selectedTable == null || selectedTable.colors == null || selectedTable.colors.Length == 0)
			{
				EditorGUILayout.HelpBox("The selected SharedColorTable has no shared colors.", MessageType.Info);
				EditorGUILayout.EndVertical();
				return false;
			}

			bool appliedSharedColor = false;
			for (int colorIndex = 0; colorIndex < selectedTable.colors.Length; colorIndex++)
			{
				OverlayColorData sharedColor = selectedTable.colors[colorIndex];
				if (sharedColor == null)
				{
					continue;
				}

				EditorGUILayout.BeginHorizontal();
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.ColorField(GUIContent.none, sharedColor.displayColor, false, true, false, GUILayout.Width(72));
				}
				EditorGUILayout.LabelField(GetSharedColorName(sharedColor, colorIndex), GUILayout.MinWidth(120));

				using (new EditorGUI.DisabledScope(ReferenceEquals(currentOverlayColorData, sharedColor)))
				{
					if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(56)))
					{
						appliedSharedColor = ApplySharedColor(property, currentOverlayColorData, sharedColor, dca);
						currentOverlayColorData = property.GetValue<OverlayColorData>();
					}
				}
				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndVertical();
			return appliedSharedColor;
		}

		private bool ApplySharedColor(SerializedProperty property, OverlayColorData currentOverlayColorData, OverlayColorData sharedColor, DynamicCharacterAvatar dca)
		{
			if (sharedColor == null)
			{
				return false;
			}

			OverlayColorData targetOverlayColorData = currentOverlayColorData ?? property.GetValue<OverlayColorData>();
			if (ReferenceEquals(targetOverlayColorData, sharedColor))
			{
				return false;
			}

			UnityEngine.Object targetObject = property.serializedObject.targetObject;
			if (targetObject != null)
			{
				Undo.RecordObject(targetObject, "Select Shared Color");
			}

			if (targetOverlayColorData != null)
			{
				targetOverlayColorData.AssignFrom(sharedColor,false, false);
				targetOverlayColorData.showDisplayColor = sharedColor.showDisplayColor;
			}
			else if (!property.SetValue(sharedColor.Clone()))
			{
				return false;
			}

			if (targetObject != null)
			{
				EditorUtility.SetDirty(targetObject);
			}

			if (dca != null && dca != targetObject)
			{
				EditorUtility.SetDirty(dca);
			}

			property.serializedObject.Update();
			return true;
		}

		private static void RefreshSharedColorTableCacheIfNeeded(bool force)
		{
			if (!force && sharedColorTableCacheInitialized && EditorApplication.timeSinceStartup < nextSharedColorTableRefreshTime)
			{
				return;
			}

			string[] sharedColorTableGuids = AssetDatabase.FindAssets("t:SharedColorTable");
			List<SharedColorTable> sharedColorTables = new List<SharedColorTable>(sharedColorTableGuids.Length);
			for (int guidIndex = 0; guidIndex < sharedColorTableGuids.Length; guidIndex++)
			{
				string sharedColorTablePath = AssetDatabase.GUIDToAssetPath(sharedColorTableGuids[guidIndex]);
				SharedColorTable sharedColorTable = AssetDatabase.LoadAssetAtPath<SharedColorTable>(sharedColorTablePath);
				if (sharedColorTable != null)
				{
					sharedColorTables.Add(sharedColorTable);
				}
			}

			sharedColorTables.Sort(CompareSharedColorTables);
			cachedSharedColorTables = sharedColorTables.ToArray();
			cachedSharedColorTableOptions = new GUIContent[cachedSharedColorTables.Length];
			for (int tableIndex = 0; tableIndex < cachedSharedColorTables.Length; tableIndex++)
			{
				SharedColorTable sharedColorTable = cachedSharedColorTables[tableIndex];
				cachedSharedColorTableOptions[tableIndex] = new GUIContent(GetSharedColorTableMenuName(sharedColorTable), AssetDatabase.GetAssetPath(sharedColorTable));
			}

			sharedColorTableCacheInitialized = true;
			nextSharedColorTableRefreshTime = EditorApplication.timeSinceStartup + SharedColorTableCacheSeconds;
		}

		private static int CompareSharedColorTables(SharedColorTable leftTable, SharedColorTable rightTable)
		{
			int nameComparison = string.Compare(GetSharedColorTableMenuName(leftTable), GetSharedColorTableMenuName(rightTable), StringComparison.OrdinalIgnoreCase);
			if (nameComparison != 0)
			{
				return nameComparison;
			}

			return string.Compare(AssetDatabase.GetAssetPath(leftTable), AssetDatabase.GetAssetPath(rightTable), StringComparison.OrdinalIgnoreCase);
		}

		private static int GetSharedColorTableIndex(SharedColorTable sharedColorTable)
		{
			for (int tableIndex = 0; tableIndex < cachedSharedColorTables.Length; tableIndex++)
			{
				if (cachedSharedColorTables[tableIndex] == sharedColorTable)
				{
					return tableIndex;
				}
			}

			return -1;
		}

		private static string GetSharedColorTableMenuName(SharedColorTable sharedColorTable)
		{
			if (sharedColorTable == null)
			{
				return "Missing Shared Color Table";
			}

			string sharedColorName = !string.IsNullOrEmpty(sharedColorTable.sharedColorName) ? sharedColorTable.sharedColorName : sharedColorTable.name;
			if (string.IsNullOrEmpty(sharedColorName))
			{
				sharedColorName = "Unnamed Shared Color Table";
			}

			if (!string.IsNullOrEmpty(sharedColorTable.name) && !string.Equals(sharedColorName, sharedColorTable.name, StringComparison.Ordinal))
			{
				return sharedColorName + " (" + sharedColorTable.name + ")";
			}

			return sharedColorName;
		}

		private static string GetSharedColorName(OverlayColorData sharedColor, int colorIndex)
		{
			if (sharedColor == null || string.IsNullOrEmpty(sharedColor.name))
			{
				return "Color " + colorIndex;
			}

			return sharedColor.name;
		}

		private static string GetPropertyStateKey(SerializedProperty property)
		{
			UnityEngine.Object targetObject = property.serializedObject.targetObject;
			int targetId = targetObject != null ? targetObject.GetInstanceID() : 0;
			return targetId.ToString() + ":" + property.propertyPath;
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
