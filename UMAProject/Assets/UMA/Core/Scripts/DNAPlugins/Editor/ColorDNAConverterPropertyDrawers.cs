using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomPropertyDrawer(typeof(ColorDNAConverterPlugin.DNAColorSet.DNAColorComponent))]
	public class ColorDNAConverterDNAColorComponentDrawer : PropertyDrawer
	{
		private float enableWidth = 30f;
		private float adjustTypeWidth = 145f;
		private List<string> _adjPopupNames = new List<string>();
		private List<GUIContent> _adjPopupNamesGUI = new List<GUIContent>();

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			OnGUI(position, property, label, false);
		}

		public void OnGUI(Rect position, SerializedProperty property, GUIContent label, bool isAlpha)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			var widthMod = position.width;
			position = EditorGUI.IndentedRect(position);
			widthMod = widthMod - position.width;
			var prevIndent = EditorGUI.indentLevel;
			var prevLabelWidth = EditorGUIUtility.labelWidth;
			EditorGUI.indentLevel = 0;

			var enableProp = property.FindPropertyRelative("enable");
			var adjustmentTypeProp = property.FindPropertyRelative("adjustmentType");
			var useDNAValueProp = property.FindPropertyRelative("useDNAValue");
			var valueProp = property.FindPropertyRelative("value");
			var adjustValueProp = property.FindPropertyRelative("adjustValue");
			var multiplierProp = property.FindPropertyRelative("multiplier");
			var enableRect = new Rect(position.xMin, position.yMin, enableWidth, EditorGUIUtility.singleLineHeight);
			var adjTypeRect = new Rect(enableRect.xMax, position.yMin, adjustTypeWidth, EditorGUIUtility.singleLineHeight);
			var useDNAValueRect = new Rect(adjTypeRect.xMax + 4f, position.yMin, position.width - EditorGUIUtility.labelWidth + widthMod, EditorGUIUtility.singleLineHeight);
			var valueRect = new Rect(enableRect.xMax, position.yMin + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, position.width - enableRect.width, EditorGUIUtility.singleLineHeight);

			EditorGUIUtility.labelWidth = 13f;
			var enableLabel = EditorGUI.BeginProperty(enableRect, new GUIContent(label.text), enableProp);
			enableProp.boolValue = EditorGUI.Toggle(enableRect, enableLabel, enableProp.boolValue);
			EditorGUI.EndProperty();
			EditorGUI.BeginDisabledGroup(!enableProp.boolValue);
			EditorGUIUtility.labelWidth = 57f;
			var adjLabel = EditorGUI.BeginProperty(adjTypeRect, new GUIContent("Adj Type"), adjustmentTypeProp);
			//we only want to show the 'BlendFactor' option in the dropdown if this is the alpha channel
			//TODO: Is the Alpha channel adjustment type *ever* anything other than 'BlendFactor'? If not, just dont show the dropdown for the channel
			if(isAlpha)
				EditorGUI.PropertyField(adjTypeRect, adjustmentTypeProp, adjLabel);
			else
			{
				if (_adjPopupNamesGUI.Count == 0)
				{
					_adjPopupNames = new List<string>(adjustmentTypeProp.enumDisplayNames);
					_adjPopupNames.Remove("Blend Factor");
					for (int i = 0; i < _adjPopupNames.Count; i++)
					{
						_adjPopupNamesGUI.Add(new GUIContent(_adjPopupNames[i]));
					}
				}
				adjLabel.tooltip = "If Absolute the setting overrides the value of the component of the color. If Adjust, the setting is added to the value of the component of the color.";
				adjustmentTypeProp.enumValueIndex = EditorGUI.Popup(adjTypeRect, adjLabel, adjustmentTypeProp.enumValueIndex, _adjPopupNamesGUI.ToArray());
			}
			EditorGUI.EndProperty();
			EditorGUIUtility.labelWidth = 65f;
			var dnaLabel = EditorGUI.BeginProperty(useDNAValueRect, new GUIContent("DNA Value"), useDNAValueProp);
			EditorGUI.PropertyField(useDNAValueRect, useDNAValueProp, dnaLabel);
			EditorGUI.EndProperty();
			EditorGUIUtility.labelWidth = 80f;
			//value Fields
			if (useDNAValueProp.boolValue)
			{
				EditorGUI.PropertyField(valueRect, multiplierProp);
			}
			else if(adjustmentTypeProp.enumValueIndex == 0 || adjustmentTypeProp.enumValueIndex == 2 || adjustmentTypeProp.enumValueIndex == 4)
			{
				EditorGUI.PropertyField(valueRect, valueProp);
			}
			else
			{
				EditorGUI.PropertyField(valueRect, adjustValueProp);
			}
			EditorGUI.EndDisabledGroup();
			EditorGUI.indentLevel = prevIndent;
			EditorGUIUtility.labelWidth = prevLabelWidth;
			EditorGUI.EndProperty();
		}
	}

	[CustomPropertyDrawer(typeof(ColorDNAConverterPlugin.DNAColorSet.DNAColorModifier))]
	public class ColorDNAConverterDNAColorModifierDrawer : PropertyDrawer
	{
		private Color _previewColor = Color.white;
		private Color _resultingColor = Color.white;
		private ColorDNAConverterDNAColorComponentDrawer CDCDCCDrawer = new ColorDNAConverterDNAColorComponentDrawer();

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			var foldoutRect = new Rect(position.xMin, position.yMin, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label);
			if (property.isExpanded)
			{
				var contentRectR = new Rect(position.xMin, position.yMin + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, position.width, EditorGUIUtility.singleLineHeight *2f);
				var contentRectG = new Rect(position.xMin, contentRectR.yMax + (EditorGUIUtility.standardVerticalSpacing * 2), position.width, EditorGUIUtility.singleLineHeight *2f);
				var contentRectB = new Rect(position.xMin, contentRectG.yMax + (EditorGUIUtility.standardVerticalSpacing * 2), position.width, EditorGUIUtility.singleLineHeight *2f);
				var contentRectA = new Rect(position.xMin, contentRectB.yMax + (EditorGUIUtility.standardVerticalSpacing * 2), position.width, EditorGUIUtility.singleLineHeight *2f);
				var previewToolsRect = new Rect(position.xMin, contentRectA.yMax + (EditorGUIUtility.standardVerticalSpacing * 2), position.width, EditorGUIUtility.singleLineHeight);
				//EditorGUI.PropertyField(contentRectR, property.FindPropertyRelative("R"));
				CDCDCCDrawer.OnGUI(contentRectR, property.FindPropertyRelative("R"), new GUIContent("R"));
				//EditorGUI.PropertyField(contentRectG, property.FindPropertyRelative("G"));
				CDCDCCDrawer.OnGUI(contentRectG, property.FindPropertyRelative("G"), new GUIContent("G"));
				//EditorGUI.PropertyField(contentRectB, property.FindPropertyRelative("B"));
				CDCDCCDrawer.OnGUI(contentRectB, property.FindPropertyRelative("B"), new GUIContent("B"));
				//EditorGUI.PropertyField(contentRectA, property.FindPropertyRelative("A"));
				CDCDCCDrawer.OnGUI(contentRectA, property.FindPropertyRelative("A"), new GUIContent("A"), true);
				var _previewProp = property.FindPropertyRelative("_testDNAVal");
				var expanded = _previewProp.isExpanded;
				GUIHelper.ToolbarStyleFoldout(previewToolsRect, new GUIContent("Preview Tool", "With the Preview Tool you can see how the above settings will affect a given color when the modifying dna evaluates to the given test value"), ref expanded, EditorStyles.label);
				_previewProp.isExpanded = expanded;
				if (_previewProp.isExpanded)
				{
					DrawPreviewTool(previewToolsRect, property, _previewProp);
				}
			}
			EditorGUI.EndProperty();
		}
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (property.isExpanded)
			{
				var _previewProp = property.FindPropertyRelative("_testDNAVal");
				var colorChannelsHeight = (EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing) * 4;
				float previewToolsHeight = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
				if (_previewProp.isExpanded)
					previewToolsHeight = ((EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 4) +9f;
				return colorChannelsHeight + previewToolsHeight + EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 4);
			}
			return EditorGUIUtility.singleLineHeight;
		}
		/// <summary>
		/// Draws a Preview Tool to assist with setting up the color modifier when not in play mode.
		/// Added because we can just use a color swatch to set the colors because they be be set to 'Adjust' and in that case the value can be negative
		/// </summary>
		private void DrawPreviewTool(Rect previewToolsRect, SerializedProperty property, SerializedProperty previewProp)
		{
			var prevIndent = EditorGUI.indentLevel;
			var toolsRect = EditorGUI.IndentedRect(previewToolsRect);
			EditorGUI.indentLevel = 0;
			if (Event.current.type == EventType.Repaint) //draw the background
			{
				var bgRect = new Rect(toolsRect.xMin - 3f, previewToolsRect.yMax, toolsRect.width + 6f, ((EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2f)) * 3f) + 3f);
				GUI.skin.box.Draw(bgRect, GUIContent.none, 0);
			}
			var previewColorRect = new Rect(toolsRect.xMin + 3f, previewToolsRect.yMax + (EditorGUIUtility.standardVerticalSpacing) + 3f, toolsRect.width - 6f, EditorGUIUtility.singleLineHeight);
			var dummyDNARect = new Rect(toolsRect.xMin + 3f, previewColorRect.yMax + (EditorGUIUtility.standardVerticalSpacing), toolsRect.width - 6f, EditorGUIUtility.singleLineHeight);
			var resultColorRect = new Rect(toolsRect.xMin + 3f, dummyDNARect.yMax + (EditorGUIUtility.standardVerticalSpacing), toolsRect.width - 6f, EditorGUIUtility.singleLineHeight);
			_previewColor = EditorGUI.ColorField(previewColorRect, new GUIContent("Preview Color", "Set a color that you would like to see changed by the settings above"), _previewColor);
			previewProp.floatValue = EditorGUI.Slider(dummyDNARect, new GUIContent("Test Evaluated DNA", "An evaluated dna result to test"), previewProp.floatValue, -1f, 1f);
			var swatchRect = EditorGUI.PrefixLabel(resultColorRect, new GUIContent("Resulting Color", "The resulting color you would get if the modifications above were applied to the preview color with the given dna result"));
			_resultingColor = EvaluatePreviewAdjustments(property, previewProp.floatValue, 1f, _previewColor);
			EditorGUIUtility.DrawColorSwatch(swatchRect, _resultingColor);
			EditorGUI.indentLevel = prevIndent;
		}
		/// <summary>
		/// Replicates how color adjustments are calculated when applied to the avatar. Returns the incoming color with adjustments applied
		/// </summary>
		private Color EvaluatePreviewAdjustments(SerializedProperty property, float dnaVal, float masterWeight, Color incomingColor)
		{
			float rAdj = 0f;
			float gAdj = 0f;
			float bAdj = 0f;
			float aAdj = 0f;
			float rCurr = 0f;
			float gCurr = 0f;
			float bCurr = 0f;
			float aCurr = 0f;
			var propR = property.FindPropertyRelative("R");
			var propG = property.FindPropertyRelative("G");
			var propB = property.FindPropertyRelative("B");
			var propA = property.FindPropertyRelative("A");
			var propREnable = propR.FindPropertyRelative("enable");
			var propGEnable = propG.FindPropertyRelative("enable");
			var propBEnable = propB.FindPropertyRelative("enable");
			var propAEnable = propA.FindPropertyRelative("enable");
			var RAdjT = propR.FindPropertyRelative("adjustmentType");
			var GAdjT = propG.FindPropertyRelative("adjustmentType");
			var BAdjT = propB.FindPropertyRelative("adjustmentType");
			var AAdjT = propA.FindPropertyRelative("adjustmentType");
			float adjustmentForChannelR = 0f;
			float adjustmentForChannelG = 0f;
			float adjustmentForChannelB = 0f;
			float adjustmentForChannelA = 0f;
			Color newColor = incomingColor;
			Vector4 newColorCalc = Vector4.zero;
			if (propREnable.boolValue)
			{
				rCurr = incomingColor.r;
				rAdj = EvaluateAdjustment(propR, dnaVal, rCurr);
				if ((RAdjT.enumNames[RAdjT.enumValueIndex].IndexOf("Absolute") > -1 && rAdj != 0) || (RAdjT.enumNames[RAdjT.enumValueIndex].IndexOf("Absolute") == -1 && rAdj != rCurr))
				{
					adjustmentForChannelR = 0f;
					if((RAdjT.enumNames[RAdjT.enumValueIndex].IndexOf("Adjust") > -1) || (RAdjT.enumNames[RAdjT.enumValueIndex].IndexOf("Additive") > -1))
					{
						adjustmentForChannelR = (incomingColor.r + rAdj) - incomingColor.r;
					}
					else
					{
						adjustmentForChannelR = rAdj - incomingColor.r;
					}
					newColor.r = Mathf.Clamp((incomingColor.r + adjustmentForChannelR), 0f, 1f);
					newColorCalc.x = incomingColor.r + adjustmentForChannelR;
				}
			}
			if (propGEnable.boolValue)
			{
				gCurr = incomingColor.g;
				gAdj = EvaluateAdjustment(propG, dnaVal, gCurr);
				if ((GAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Absolute") > -1 && gAdj != 0) || (GAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Absolute") == -1 && gAdj != gCurr))
				{
					adjustmentForChannelG = 0f;
					if ((GAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Adjust") > -1) || (GAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Additive") > -1))
					{
						adjustmentForChannelG = (incomingColor.g + gAdj) - incomingColor.g;
					}
					else
					{
						adjustmentForChannelG = gAdj - incomingColor.g;
					}
					newColor.g = Mathf.Clamp((incomingColor.g + adjustmentForChannelG), 0f, 1f);
					newColorCalc.y = incomingColor.g + adjustmentForChannelG;
				}
			}
			if (propBEnable.boolValue)
			{
				bCurr = incomingColor.b;
				bAdj = EvaluateAdjustment(propB, dnaVal, bCurr);
				if ((BAdjT.enumNames[BAdjT.enumValueIndex].IndexOf("Absolute") > -1 && bAdj != 0) || (BAdjT.enumNames[BAdjT.enumValueIndex].IndexOf("Absolute") == -1 && bAdj != bCurr))
				{
					adjustmentForChannelB = 0f;
					if ((BAdjT.enumNames[BAdjT.enumValueIndex].IndexOf("Adjust") > -1) || (BAdjT.enumNames[BAdjT.enumValueIndex].IndexOf("Additive") > -1))
					{
						adjustmentForChannelB = (incomingColor.b + bAdj) - incomingColor.b;
					}
					else
					{
						adjustmentForChannelB = bAdj - incomingColor.b;
					}
					newColor.b = Mathf.Clamp((incomingColor.b + adjustmentForChannelB), 0f, 1f);
					newColorCalc.z = incomingColor.b + adjustmentForChannelB;
				}
			}
			if (propAEnable.boolValue)
			{
				aCurr = incomingColor.a;
				aAdj = EvaluateAdjustment(propA, dnaVal, aCurr);
				if ((AAdjT.enumNames[AAdjT.enumValueIndex].IndexOf("Absolute") > -1 && aAdj != 0) || (AAdjT.enumNames[AAdjT.enumValueIndex].IndexOf("Absolute") == -1 && aAdj != aCurr))
				{
					adjustmentForChannelA = 0f;
					if ((AAdjT.enumNames[AAdjT.enumValueIndex].IndexOf("Adjust") > -1))
					{
						adjustmentForChannelA = (incomingColor.a + aAdj) - incomingColor.a;
					}
					else
					{
						adjustmentForChannelA = aAdj - incomingColor.a;
					}
					newColor.a = Mathf.Clamp((incomingColor.a + adjustmentForChannelA), 0f, 1f);
				}
			}
			if(RAdjT.enumNames[RAdjT.enumValueIndex].IndexOf("Additive") > -1 || GAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Additive") > -1 || BAdjT.enumNames[BAdjT.enumValueIndex].IndexOf("Additive") > -1)
			{
				if(RAdjT.enumNames[RAdjT.enumValueIndex].IndexOf("Additive") > -1 && newColorCalc.x > 1)
				{
					newColor.g = newColor.g / newColorCalc.x;
					newColor.b = newColor.b / newColorCalc.x;
				}
				if (GAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Additive") > -1 && newColorCalc.y > 1)
				{
					newColor.r = newColor.r / newColorCalc.y;
					newColor.b = newColor.b / newColorCalc.y;
				}
				if (BAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Additive") > -1 && newColorCalc.z > 1)
				{
					newColor.r = newColor.r / newColorCalc.z;
					newColor.g = newColor.g / newColorCalc.z;
				}
				if (RAdjT.enumNames[RAdjT.enumValueIndex].IndexOf("Additive") > -1 && newColorCalc.x > 1)
				{
					newColor.r = 1f;
				}
				if (GAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Additive") > -1 && newColorCalc.y > 1)
				{
					newColor.g = 1f;
				}
				if (BAdjT.enumNames[GAdjT.enumValueIndex].IndexOf("Additive") > -1 && newColorCalc.z > 1)
				{
					newColor.b = 1f;
				}
			}
			return newColor;
		}
		/// <summary>
		/// Evaluates the color adjustment for a component of a color and returns the incoming component adjusted
		/// </summary>
		/// <param name="property">The Serialized property for the DNAColorComponent to use to adjust the current color component</param>
		private float EvaluateAdjustment(SerializedProperty property, float dnaValue, float currentColor)
		{
			var useDNAValue = property.FindPropertyRelative("useDNAValue").boolValue;
			var adjustmentType = property.FindPropertyRelative("adjustmentType");
			var value = property.FindPropertyRelative("value").floatValue;
			var adjustValue = property.FindPropertyRelative("adjustValue").floatValue;
			var multiplier = property.FindPropertyRelative("multiplier").floatValue;
			if (useDNAValue)
			{
				if (adjustmentType.enumNames[adjustmentType.enumValueIndex].IndexOf("Absolute") > -1 && adjustmentType.enumNames[adjustmentType.enumValueIndex].IndexOf("Additive") == -1)
					return Mathf.Lerp(currentColor, Mathf.Clamp(dnaValue * multiplier, 0f, 1f), Mathf.Clamp(dnaValue, 0f, 1f));
				else if (adjustmentType.enumNames[adjustmentType.enumValueIndex].IndexOf("BlendFactor") > -1)
					return Mathf.Clamp(dnaValue * multiplier, 0f, 1f);
				else
					return Mathf.Lerp(0f, dnaValue * multiplier, Mathf.Abs(dnaValue));
			}
			else
			{
				if (adjustmentType.enumNames[adjustmentType.enumValueIndex].IndexOf("Absolute") > -1 && adjustmentType.enumNames[adjustmentType.enumValueIndex].IndexOf("Additive") == -1)
					return Mathf.Lerp(currentColor, value, Mathf.Clamp(dnaValue, 0f, 1f));
				else if (adjustmentType.enumNames[adjustmentType.enumValueIndex].IndexOf("BlendFactor") > -1)
					return Mathf.Lerp(0f, value, Mathf.Abs(dnaValue));
				else
					return Mathf.Lerp(0f, adjustValue, Mathf.Abs(dnaValue));
			}
		}
	}

}
