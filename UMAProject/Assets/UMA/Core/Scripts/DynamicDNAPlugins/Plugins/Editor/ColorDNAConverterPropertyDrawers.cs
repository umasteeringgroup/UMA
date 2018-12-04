using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomPropertyDrawer(typeof(ColorDNAConverterPlugin.DNAColorSet.DNAColorComponent))]
	public class ColorDNAConverterDNAColorComponentDrawer : PropertyDrawer
	{
		private float enableWidth = 30f;
		private float adjustTypeWidth = 145f;
		//here we want to simplify the drawing on each line
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
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
			//var useDNAValueRect = new Rect(enableRect.xMax, position.yMin, useDNAWidth, EditorGUIUtility.singleLineHeight);
			//var adjTypeRect = new Rect(position.xMin + EditorGUIUtility.labelWidth - widthMod, position.yMin, position.width - EditorGUIUtility.labelWidth + widthMod, EditorGUIUtility.singleLineHeight);
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
			EditorGUI.PropertyField(adjTypeRect, adjustmentTypeProp, adjLabel);
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

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
		}
	}

	[CustomPropertyDrawer(typeof(ColorDNAConverterPlugin.DNAColorSet.DNAColorModifier))]
	public class ColorDNAConverterDNAColorModifierDrawer : PropertyDrawer
	{
		//draw a foldout for the DNAColorModifier but also draw a color picker there that changes the values of DNAColorModifier
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			var foldoutRect = new Rect(position.xMin, position.yMin, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
			//TODO Figure out how to make the colorpicker useful rather than confusing when channels are set to 'Adjust' modes
			//Maybe some kind of 'Preview Tool' where you can set the incoming color, a dummy dna value and see the result of the settings
			//for now I'm gonna disable it
			/*var colorPickerRect = new Rect(foldoutRect.xMax, position.yMin, position.width - foldoutRect.width, EditorGUIUtility.singleLineHeight);
			var propRVal = property.FindPropertyRelative("R").FindPropertyRelative("value");
			var propGVal = property.FindPropertyRelative("G").FindPropertyRelative("value");
			var propBVal = property.FindPropertyRelative("B").FindPropertyRelative("value");
			var propAVal = property.FindPropertyRelative("A").FindPropertyRelative("value");
			
			var setColor = new Color(propRVal.floatValue, propGVal.floatValue, propBVal.floatValue, propAVal.floatValue);
			EditorGUI.BeginChangeCheck();
			var prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;
			setColor = EditorGUI.ColorField(colorPickerRect, setColor);
			EditorGUI.indentLevel = prevIndent;
			if (EditorGUI.EndChangeCheck())
			{
				propRVal.floatValue = setColor.r;
				propGVal.floatValue = setColor.g;
				propBVal.floatValue = setColor.b;
				propAVal.floatValue = setColor.a;
				property.serializedObject.ApplyModifiedProperties();
			}*/
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label);
			if (property.isExpanded)
			{
				var contentRectR = new Rect(position.xMin, position.yMin + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, position.width, EditorGUIUtility.singleLineHeight *2f);
				var contentRectG = new Rect(position.xMin, contentRectR.yMax + (EditorGUIUtility.standardVerticalSpacing * 2), position.width, EditorGUIUtility.singleLineHeight *2f);
				var contentRectB = new Rect(position.xMin, contentRectG.yMax + (EditorGUIUtility.standardVerticalSpacing * 2), position.width, EditorGUIUtility.singleLineHeight *2f);
				var contentRectA = new Rect(position.xMin, contentRectB.yMax + (EditorGUIUtility.standardVerticalSpacing * 2), position.width, EditorGUIUtility.singleLineHeight *2f);
				EditorGUI.PropertyField(contentRectR, property.FindPropertyRelative("R"));
				EditorGUI.PropertyField(contentRectG, property.FindPropertyRelative("G"));
				EditorGUI.PropertyField(contentRectB, property.FindPropertyRelative("B"));
				EditorGUI.PropertyField(contentRectA, property.FindPropertyRelative("A"));
			}
			EditorGUI.EndProperty();
		}
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (property.isExpanded)
			{
				var colorChannelsHeight = (EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing) * 4;
				return colorChannelsHeight + EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 4);
			}
			return EditorGUIUtility.singleLineHeight;
		}
	}

}
