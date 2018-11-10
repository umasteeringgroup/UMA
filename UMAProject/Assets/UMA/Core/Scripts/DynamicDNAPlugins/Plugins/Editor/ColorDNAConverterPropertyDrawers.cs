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
		private float useDNAWidth = 105f;
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
			var useDNAValueProp = property.FindPropertyRelative("useDNAValue");
			var valueProp = property.FindPropertyRelative("value");
			var multiplierProp = property.FindPropertyRelative("multiplier");
			var enableRect = new Rect(position.xMin, position.yMin, enableWidth, position.height);
			var useDNAValueRect = new Rect(enableRect.xMax, position.yMin, useDNAWidth, position.height);
			var valueRect = new Rect(position.xMin + EditorGUIUtility.labelWidth - widthMod, position.yMin, position.width - EditorGUIUtility.labelWidth + widthMod, position.height);

			EditorGUIUtility.labelWidth = 13f;
			var enableLabel = EditorGUI.BeginProperty(enableRect, new GUIContent(label.text), enableProp);
			enableProp.boolValue = EditorGUI.Toggle(enableRect, enableLabel, enableProp.boolValue);
			EditorGUI.EndProperty();
			EditorGUI.BeginDisabledGroup(!enableProp.boolValue);
			EditorGUIUtility.labelWidth = 95f;
			EditorGUI.PropertyField(useDNAValueRect, useDNAValueProp);
			EditorGUIUtility.labelWidth = 60f;
			if (useDNAValueProp.boolValue)//Value
			{
				EditorGUI.PropertyField(valueRect, multiplierProp);
			}
			else//Weight
			{
				EditorGUI.PropertyField(valueRect, valueProp);
			}
			EditorGUI.EndDisabledGroup();
			EditorGUI.indentLevel = prevIndent;
			EditorGUIUtility.labelWidth = prevLabelWidth;
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUIUtility.singleLineHeight;
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
			var colorPickerRect = new Rect(foldoutRect.xMax, position.yMin, position.width - foldoutRect.width, EditorGUIUtility.singleLineHeight);
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
			}
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label);
			if (property.isExpanded)
			{
				var contentRectR = new Rect(position.xMin, position.yMin + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, position.width, EditorGUIUtility.singleLineHeight);
				var contentRectG = new Rect(position.xMin, contentRectR.yMax, position.width, EditorGUIUtility.singleLineHeight);
				var contentRectB = new Rect(position.xMin, contentRectG.yMax, position.width, EditorGUIUtility.singleLineHeight);
				var contentRectA = new Rect(position.xMin, contentRectB.yMax, position.width, EditorGUIUtility.singleLineHeight);
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
				return EditorGUIUtility.singleLineHeight * 5 + EditorGUIUtility.standardVerticalSpacing;
			}
			return EditorGUIUtility.singleLineHeight;
		}
	}

}
