using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

namespace UMA.CharacterSystem.Editors
{
	[CustomPropertyDrawer(typeof(SkeletonModifier))]
	public class SkeletonModifierPropertyDrawer : PropertyDrawer
	{
		float padding = 4f;
		public List<string> hashNames = new List<string>();
		//used when editing in playmode has the actual boneHashes from the avatars skeleton
		public List<string> bonesInSkeleton = null;
		public List<int> hashes = new List<int>();
		public string[] dnaNames;

		private bool _allowLegacyDNADrawer = false;

		public bool AllowLegacyDNADrawer
		{
			get { return _allowLegacyDNADrawer; }
			set { _allowLegacyDNADrawer = value; }
		}

#pragma warning disable 618 //disable obsolete warning
		//we use DNAEvaluatorList now
		spValModifierPropertyDrawer thisSpValDrawer = null;
#pragma warning restore 618

		Texture warningIcon;
		GUIStyle warningStyle;

		//Rects
		Rect tabsArea;
		Rect valMinMaxArea;
		Rect valBox;
		Rect valLabel;
		Rect valVal;
		Rect minBox;
		Rect minLabel;
		Rect minVal;
		Rect maxBox;
		Rect maxLabel;
		Rect maxVal;
		Rect modifiersArea;
		Rect modifiersProps;
		Rect modifiersDel;
		Rect modifiersAdd;

		int activeTab = 0;
		string[] tabsLabels = new string[] { "ValuesX", "ValuesY", "ValuesZ" };
		float valLabelWidth = 48f;
		float minMaxLabelWidth = 38f;
		float delButWidth = 20f;
		float addButWidth = 60f;
		GUIContent valueLabel = new GUIContent("X Value", "The Initial Value used at the start of the calculation. For Scale this should usually be 1, for Position or Rotation, should usually be 0");
		GUIContent valueOverrideLabel = new GUIContent("Intitial X Value Override", "Editing this will affect the starting shape of ALL characters that use this modifiers converter. Consider using a Starting UMABonePose instead as these have their own tools, and the weight of the pose can be controlled by dna 'per character'");

		private bool _initialized = false;

		public bool Init(string[] _dnaNames = null)
		{
			Init(new List<string>(), new List<int>(), _dnaNames);
			return _initialized;
		}
#pragma warning disable 618 //disable obsolete warning
		public bool Init(List<string> _hashNames, List<int> _hashes, string[] _dnaNames = null)
		{
			if (!_initialized)
			{

				hashNames = _hashNames;
				hashes = _hashes;
				dnaNames = _dnaNames;

				//Will be removed ina future version since we use DNAEvaluatorList for these now
				if (_allowLegacyDNADrawer)
				{
					if (thisSpValDrawer == null)
						thisSpValDrawer = new spValModifierPropertyDrawer();
					thisSpValDrawer.dnaNames = dnaNames;
				}

				if (warningIcon == null)
				{
					warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
					warningStyle = new GUIStyle(EditorStyles.label);
					warningStyle.fixedHeight = warningIcon.height + 4f;
					warningStyle.contentOffset = new Vector2(0, -2f);
				}
				valueOverrideLabel.image = warningIcon;
				_initialized = true;
			}
			else
			{
				if(_dnaNames != null)
					UpdateDnaNames(_dnaNames);
			}
			return _initialized;
		}
#pragma warning restore 618 //restore obsolete warning

		public void UpdateHashNames(List<string> _hashNames, List<int> _hashes)
		{
			hashNames = _hashNames;
			hashes = _hashes;
		}
		public void UpdateDnaNames(string[] newDnaNames)
		{
			if (thisSpValDrawer != null)
				thisSpValDrawer.dnaNames = newDnaNames;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{

			if (!Init(hashNames, hashes))
				return;

			int startingIndent = EditorGUI.indentLevel;

			EditorGUI.BeginProperty(position, label, property);

			string thisHashName = property.FindPropertyRelative("_hashName").stringValue;

			var currRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);

			string betterLabel = label.text;
			if (property.FindPropertyRelative("_property").enumDisplayNames[property.FindPropertyRelative("_property").enumValueIndex] != "")
			{
				betterLabel += " (" + property.FindPropertyRelative("_property").enumDisplayNames[property.FindPropertyRelative("_property").enumValueIndex] + ")";
			}

			List<string> boneNames = new List<string>();
			if (bonesInSkeleton != null)
			{
				boneNames = new List<string>(bonesInSkeleton);
			}
			else
			{
				boneNames = new List<string>(hashNames);
			}

			int hashNameIndex = boneNames.IndexOf(thisHashName);
			//Warn about current Bone not being available in active Avatar if we are in playMode (i.e. got sent BonesInSkeleton)
			if (hashNameIndex == -1 && bonesInSkeleton != null)
			{
				boneNames.Insert(0, thisHashName + " (missing)");
				hashNameIndex = 0;
				var warningRect = new Rect((currRect.xMin), currRect.yMin, 20f, currRect.height);
				var warningIconGUI = new GUIContent("", thisHashName + " was not a bone in the Avatars Skeleton. Please choose another bone for this modifier or delete it.");
				warningIconGUI.image = warningIcon;
				betterLabel += " (missing)";
				GUI.Label(warningRect, warningIconGUI, warningStyle);
			}
			//Draw the foldout- toolbar-ish style here?
			property.isExpanded = EditorGUI.Foldout(currRect, property.isExpanded, betterLabel, true);
			if (property.isExpanded)
			{
				EditorGUI.indentLevel++;

				//THE BONE NAME FIELD
				currRect = new Rect(currRect.xMin, currRect.yMax + padding, currRect.width, EditorGUIUtility.singleLineHeight);
				if (boneNames.Count > 0)
				{
					int newHashNameIndex = hashNameIndex;
					EditorGUI.BeginChangeCheck();
					newHashNameIndex = EditorGUI.Popup(currRect, "Bone Name", hashNameIndex, boneNames.ToArray());
					if (EditorGUI.EndChangeCheck())
					{
						if (newHashNameIndex != hashNameIndex)
						{
							property.FindPropertyRelative("_hashName").stringValue = boneNames[newHashNameIndex];
							property.FindPropertyRelative("_hash").intValue = UMAUtils.StringToHash(boneNames[newHashNameIndex]);
							property.serializedObject.ApplyModifiedProperties();
						}
					}
				}
				else
				{
					//make sure the hash is changed if the name is edited
					EditorGUI.BeginChangeCheck();
					EditorGUI.PropertyField(currRect, property.FindPropertyRelative("_hashName"), new GUIContent("Bone Name"));
					if (EditorGUI.EndChangeCheck())
					{
						property.FindPropertyRelative("_hash").intValue = UMAUtils.StringToHash(property.FindPropertyRelative("_hashName").stringValue);
					}
				}

				//THE PROPERTY FIELD
				currRect = new Rect(currRect.xMin, currRect.yMax + padding, currRect.width, EditorGUIUtility.singleLineHeight);
				EditorGUI.PropertyField(currRect, property.FindPropertyRelative("_property"));


				//X/Y/Z VALUES TABS

				SerializedProperty subValsToOpen = null;
				var valuesX = property.FindPropertyRelative("_valuesX");
				var valuesY = property.FindPropertyRelative("_valuesY");
				var valuesZ = property.FindPropertyRelative("_valuesZ");

				tabsArea = currRect = new Rect(currRect.xMin + (EditorGUI.indentLevel * 10f), currRect.yMax + padding, currRect.width - (EditorGUI.indentLevel * 10f), EditorGUIUtility.singleLineHeight);

				//activeTab = valuesX.isExpanded ? 0 : (valuesY.isExpanded ? 1 : (valuesZ.isExpanded ? 2 : 0));
				//subValsToOpen = valuesX.isExpanded ? valuesX : (valuesY.isExpanded ? valuesY : (valuesZ.isExpanded ? valuesZ : valuesX));
				activeTab = 0;
				subValsToOpen = valuesX;
				valueLabel.text = "X Value";
				valueOverrideLabel.text = "Intitial X Value Override";
				if (!valuesX.isExpanded)
				{
					if (valuesY.isExpanded)
					{
						activeTab = 1;
						subValsToOpen = valuesY;
						valueLabel.text = "Y Value";
						valueOverrideLabel.text = "Intitial Y Value Override";
					}
					else if (valuesZ.isExpanded)
					{
						activeTab = 2;
						subValsToOpen = valuesZ;
						valueLabel.text = "Z Value";
						valueOverrideLabel.text = "Intitial Z Value Override";
					}
					else
					{
						valuesX.isExpanded = true;
					}
				}

				EditorGUI.BeginChangeCheck();
				activeTab = GUI.Toolbar(tabsArea, activeTab, tabsLabels, EditorStyles.toolbarButton);
				if (EditorGUI.EndChangeCheck())
				{
					//make sure any focussed text areas dont prevent the tab from switching
					GUI.FocusControl(null);
					if (activeTab == 0)
					{
						valuesX.isExpanded = true;
						valuesY.isExpanded = false;
						valuesZ.isExpanded = false;
						valueLabel.text = "X Value";
						valueOverrideLabel.text = "Intitial X Value Override";
					}
					else if (activeTab == 1)
					{
						valuesX.isExpanded = false;
						valuesY.isExpanded = true;
						valuesZ.isExpanded = false;
						subValsToOpen = valuesY;
						valueLabel.text = "Y Value";
						valueOverrideLabel.text = "Intitial Y Value Override";
					}
					else if (activeTab == 2)
					{
						valuesX.isExpanded = false;
						valuesY.isExpanded = false;
						valuesZ.isExpanded = true;
						subValsToOpen = valuesZ;
						valueLabel.text = "Z Value";
						valueOverrideLabel.text = "Intitial Z Value Override";
					}
				}

				//VALUES TAB CONTENT
				if (subValsToOpen != null)
				{
					var subValuesVal = subValsToOpen.FindPropertyRelative("_val");
					var subValuesMin = subValsToOpen.FindPropertyRelative("_min");
					var subValuesMax = subValsToOpen.FindPropertyRelative("_max");

					valMinMaxArea = currRect = new Rect(tabsArea.xMin, tabsArea.yMax, tabsArea.width, EditorGUIUtility.singleLineHeight + (padding * 4f));
					valBox = new Rect(valMinMaxArea.xMin, valMinMaxArea.yMin + (padding * 2f), valMinMaxArea.width / 3f, valMinMaxArea.height - (padding * 4f));
					minBox = new Rect(valBox.xMax, valBox.yMin, valBox.width, valBox.height);
					maxBox = new Rect(minBox.xMax, minBox.yMin, minBox.width, minBox.height);
					valLabel = new Rect(valBox.xMin + (padding * 6f), valBox.yMin, valLabelWidth, valBox.height);
					valVal = new Rect(valLabel.xMax + (padding), valLabel.yMin, (valBox.width - valLabelWidth) - (padding * 10f), valLabel.height);
					minLabel = new Rect(minBox.xMin + (padding * 2f), minBox.yMin, minMaxLabelWidth, minBox.height);
					minVal = new Rect(minLabel.xMax, minLabel.yMin, (minBox.width - minMaxLabelWidth) - (padding * 4f), minLabel.height);
					maxLabel = new Rect(maxBox.xMin + (padding * 2f), maxBox.yMin, minMaxLabelWidth, maxBox.height);
					maxVal = new Rect(maxLabel.xMax, maxLabel.yMin, (maxBox.width - minMaxLabelWidth) - (padding * 4f), maxLabel.height);

					if (subValuesVal.isExpanded)
					{
						valMinMaxArea.height += EditorGUIUtility.singleLineHeight + padding;
					}
					//VALUE/MIN/MAX FIELDS

					EditorGUI.DrawRect(valMinMaxArea, new Color32(255, 255, 255, 100));
					var prevIndent = EditorGUI.indentLevel;
					EditorGUI.indentLevel = 0;

					//Value Field

					//Show another line here if this is expanded that lets the user change the starting value
					subValuesVal.isExpanded = EditorGUI.Foldout(valLabel, subValuesVal.isExpanded, valueLabel, true);
					EditorGUI.BeginDisabledGroup(true);
					subValuesVal.FindPropertyRelative("_value").floatValue = EditorGUI.FloatField(valVal, subValuesVal.FindPropertyRelative("_value").floatValue);
					EditorGUI.EndDisabledGroup();
					if (subValuesVal.isExpanded)
					{
						var subValuesValRect = new Rect(tabsArea.xMin + (padding * 4f), tabsArea.yMax + (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 8), tabsArea.width - (padding * 8f), EditorGUIUtility.singleLineHeight);
						subValuesVal.FindPropertyRelative("_value").floatValue = EditorGUI.FloatField(subValuesValRect, valueOverrideLabel, subValuesVal.FindPropertyRelative("_value").floatValue);
					}

					//Min Field
					EditorGUI.LabelField(minLabel, "Min");
					subValuesMin.floatValue = EditorGUI.FloatField(minVal, subValuesMin.floatValue);

					//Max Field
					EditorGUI.LabelField(maxLabel, "Max");
					subValuesMax.floatValue = EditorGUI.FloatField(maxVal, subValuesMax.floatValue);
					//EditorGUI.indentLevel = prevIndent;

					//VALUE MODIFIERS AREA

					var legacyModifiersProp = subValuesVal.FindPropertyRelative("_modifiers");
					var legacyModifiersCount = legacyModifiersProp.arraySize;

					var modifyingDNAProp = subValuesVal.FindPropertyRelative("_modifyingDNA");
					var modifyingDNACount = modifyingDNAProp.FindPropertyRelative("_dnaEvaluators").arraySize;

					currRect = new Rect(currRect.xMin, valMinMaxArea.yMax + 2f, currRect.width, EditorGUIUtility.singleLineHeight);
					modifiersArea = new Rect(currRect.xMin, currRect.yMin, currRect.width, ((EditorGUIUtility.singleLineHeight + padding) * (legacyModifiersCount + 2)));//plus 2 for label and add button

					if(modifyingDNACount != 0 || !_allowLegacyDNADrawer)
					{
						modifiersArea = new Rect(currRect.xMin, currRect.yMin, currRect.width, (EditorGUI.GetPropertyHeight(modifyingDNAProp) + padding));
					}

					EditorGUI.DrawRect(modifiersArea, new Color32(255, 255, 255, 100));

					//Pad the current Rect
					currRect.xMin += padding * 2f;
					currRect.width -= padding * 2f;

					//When modifiers get upgraded to _modifyingDNA they get cleared
					//But for now
					if (modifyingDNACount == 0 && _allowLegacyDNADrawer)
					{
						
						//EditorGUI.indentLevel++;
						EditorGUI.LabelField(currRect, "Value Modifiers");
						//EditorGUI.indentLevel--;

						//Draw modifiers list
						for (int i = 0; i < legacyModifiersCount; i++)
						{
							currRect = new Rect(currRect.xMin, currRect.yMax + padding, currRect.width, EditorGUIUtility.singleLineHeight);
							modifiersProps = new Rect(currRect.xMin, currRect.yMin, currRect.width - delButWidth, EditorGUIUtility.singleLineHeight);
							modifiersDel = new Rect(modifiersProps.xMax, currRect.yMin, delButWidth, EditorGUIUtility.singleLineHeight);
							thisSpValDrawer.OnGUI(modifiersProps, legacyModifiersProp.GetArrayElementAtIndex(i), new GUIContent(""));
							if (GUI.Button(modifiersDel, "X"))
							{
								legacyModifiersProp.DeleteArrayElementAtIndex(i);
							}
						}

						//Draw the add button
						modifiersAdd = new Rect(currRect.xMax - addButWidth, currRect.yMax + padding, addButWidth, EditorGUIUtility.singleLineHeight);

						if (GUI.Button(modifiersAdd, "Add"))
						{
							legacyModifiersProp.InsertArrayElementAtIndex(legacyModifiersCount);
						}
						legacyModifiersProp.serializedObject.ApplyModifiedProperties();
					}
					else
					{
						var thisModifyingDNARect = new Rect(currRect.xMin, currRect.yMin, currRect.width, position.height - currRect.yMax);
						EditorGUI.PropertyField(thisModifyingDNARect, modifyingDNAProp);
					}
					
				}

			}
			EditorGUI.indentLevel = startingIndent;
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			float h = EditorGUIUtility.singleLineHeight + padding;
			int extraLines = 1;
			if (property.isExpanded)
			{
				extraLines = extraLines + 3;
				var valuesX = property.FindPropertyRelative("_valuesX");
				var valuesY = property.FindPropertyRelative("_valuesY");
				var valuesZ = property.FindPropertyRelative("_valuesZ");
				//valuesX is always expanded now if nothing else is
				//if (valuesX.isExpanded || valuesY.isExpanded || valuesZ.isExpanded)
				//{
				extraLines++;
				var activeValues = valuesX.isExpanded ? valuesX : (valuesY.isExpanded ? valuesY : (valuesZ.isExpanded ? valuesZ : valuesX));
				var subValuesVal = activeValues.FindPropertyRelative("_val");
				if (subValuesVal.isExpanded)
					extraLines++;
				
				
				var modifyingDNAProp = subValuesVal.FindPropertyRelative("_modifyingDNA");
				var modifyingDNACount = modifyingDNAProp.FindPropertyRelative("_dnaEvaluators").arraySize;
				var legacyModifiersCount = subValuesVal.FindPropertyRelative("_modifiers").arraySize;
				//When modifiers get upgraded to _modifyingDNA they get cleared
				//But for now
				if (modifyingDNACount == 0 && _allowLegacyDNADrawer)
				{
					extraLines++;
					extraLines += legacyModifiersCount;
					//extrapix = 10f;
					extraLines++;
					extraLines++;
				}
				/*else
				{
					//Add _modifyingDNA (DNAEvaluators)
					extraLines += modifyingDNACount + 3;
				}*/
				
				//}
				h *= (extraLines);
				if(modifyingDNACount != 0 || !_allowLegacyDNADrawer)
				{
					h += (EditorGUI.GetPropertyHeight(modifyingDNAProp) + padding) + padding *3;
				}
			}
			return h;
		}
	}
	#region LEGACY MODIFIERS DRAWER

	[CustomPropertyDrawer(typeof(SkeletonModifier.spVal.spValValue.spValModifier))]
	[System.Obsolete("Do not use. Will be removed in a future version")]
	public class spValModifierPropertyDrawer : PropertyDrawer
	{
		public string[] dnaNames;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			int startingIndent = EditorGUI.indentLevel;
			EditorGUI.BeginProperty(position, label, property);
			var valR = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
			var ddOne = valR;
			var ddTwo = valR;
			ddOne.width = valR.width / 2f + ((EditorGUI.indentLevel - 1) * 10);
			ddTwo.width = valR.width / 2f - ((EditorGUI.indentLevel - 1) * 10);
			ddTwo.x = ddTwo.x + ddOne.width;
			var modifieri = property.FindPropertyRelative("_modifier").enumValueIndex;
			EditorGUI.PropertyField(ddOne, property.FindPropertyRelative("_modifier"), new GUIContent(""));
			EditorGUI.indentLevel = 0;
			if (modifieri > 3)
			{
				string currentVal = property.FindPropertyRelative("_DNATypeName").stringValue;

				if (dnaNames == null || dnaNames.Length == 0)
				{
					//If there are no names show a field with the dna name in it with a warning tooltip
					//NOPE we should just show a manual field for this
					/*EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.LabelField(ddTwo, new GUIContent(property.FindPropertyRelative("_DNATypeName").stringValue, "You do not have any DNA Names set up in your DNA asset. Add some names for the Skeleton Modifiers to use."), EditorStyles.textField);
                    EditorGUI.EndDisabledGroup();*/
					currentVal = EditorGUI.TextField(ddTwo, currentVal);
				}
				else
				{
					int selectedIndex = -1;
					List<GUIContent> niceDNANames = new List<GUIContent>();
					niceDNANames.Add(new GUIContent("None"));
					bool missing = currentVal != "";
					for (int i = 0; i < dnaNames.Length; i++)
					{
						niceDNANames.Add(new GUIContent(dnaNames[i]));
						if (dnaNames[i] == currentVal)
						{
							selectedIndex = i;
							missing = false;
						}
					}
					if (missing)
					{
						niceDNANames[0].text = currentVal + " (missing)";
						niceDNANames[0].tooltip = currentVal + " was not in the DNAAssets names list. This modifier wont do anything until you change the dna name it uses or you add this name to your DNA Asset names.";
					}
					int newSelectedIndex = selectedIndex == -1 ? 0 : selectedIndex + 1;
					EditorGUI.BeginChangeCheck();
					newSelectedIndex = EditorGUI.Popup(ddTwo, newSelectedIndex, niceDNANames.ToArray());
					if (EditorGUI.EndChangeCheck())
					{
						//if its actually changed
						if (newSelectedIndex != selectedIndex + 1)
						{
							if (newSelectedIndex == 0)
							{
								if (niceDNANames[0].text.IndexOf("(missing) ") < 0)
									property.FindPropertyRelative("_DNATypeName").stringValue = "";
							}
							else
							{
								property.FindPropertyRelative("_DNATypeName").stringValue = dnaNames[newSelectedIndex - 1];
							}
						}
					}
				}
			}
			else
			{
				EditorGUI.PropertyField(ddTwo, property.FindPropertyRelative("_modifierValue"), new GUIContent(""));
			}
			EditorGUI.indentLevel = startingIndent;
			EditorGUI.EndProperty();
		}
	}
	#endregion
}
