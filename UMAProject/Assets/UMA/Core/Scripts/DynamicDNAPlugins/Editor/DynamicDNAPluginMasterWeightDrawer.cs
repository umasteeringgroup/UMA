﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA
{
	//this is literally just to put two extra pixels height in for a MasterWeight field- yeah I'm OCD'ing here
	[CustomPropertyDrawer(typeof(DynamicDNAPlugin.MasterWeight),true)]
	public class DynamicDNAPluginMasterWeightDrawer : PropertyDrawer
	{

		private DNAEvaluationGraph dummyEvaluator = new DNAEvaluationGraph();
		private GUIStyle italicLabel;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (italicLabel == null)
			{
				italicLabel = new GUIStyle(EditorStyles.label);
				italicLabel.clipping = TextClipping.Clip;
				italicLabel.fontStyle = FontStyle.Italic;
			}

			var foldoutRect = new Rect(position.xMin + 6f, position.yMin, position.width - 6f, EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2));

			var foldoutLabel = EditorGUI.BeginProperty(foldoutRect, new GUIContent(property.displayName), property);
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, foldoutLabel, true);
			EditorGUI.EndProperty();

			if (property.isExpanded)
			{
				var globalWeightProp = property.FindPropertyRelative("_globalWeight");
				var dnaForWeightProp = property.FindPropertyRelative("_DNAForWeight");

				var rect = EditorGUI.IndentedRect(foldoutRect);
				var field1Rect = new Rect(rect.xMin, foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing, rect.width, EditorGUI.GetPropertyHeight(globalWeightProp));
				var field2Rect = new Rect(field1Rect.xMin, field1Rect.yMax, field1Rect.width, EditorGUI.GetPropertyHeight(dnaForWeightProp));

				EditorGUI.PropertyField(field1Rect, globalWeightProp);
				EditorGUI.PropertyField(field2Rect, dnaForWeightProp);
			}

			DrawCurrentSettingInfo(property, foldoutRect, position);

		}

		private void DrawCurrentSettingInfo(SerializedProperty property, Rect foldoutRect, Rect position)
		{
			var foldoutLabelNameRect = new Rect(foldoutRect.xMin + 9f, foldoutRect.yMin, 70f, foldoutRect.height);
			var foldoutInfoRect = new Rect(foldoutLabelNameRect.xMax + 4f, foldoutRect.yMin, 170f, foldoutRect.height);
			var foldoutFieldRect = new Rect(foldoutInfoRect.xMax + 4f, foldoutRect.yMin, 40f, EditorGUIUtility.singleLineHeight);

			var globalWeightProp = property.FindPropertyRelative("_globalWeight");
			var dnaForWeightProp = property.FindPropertyRelative("_DNAForWeight");
			var dnaNameForWeightProp = dnaForWeightProp.FindPropertyRelative("_dnaName");

			GUIContent infoText = dnaNameForWeightProp.stringValue == "" ? new GUIContent("Using Global Weight : ") : new GUIContent("Using DNA name '" + dnaNameForWeightProp.stringValue + "' : ");

			//recalc widths based on that
			var labelWidth = (EditorStyles.foldout.CalcSize(new GUIContent(property.displayName)).x - 15f);
			foldoutLabelNameRect.width = labelWidth;
			foldoutInfoRect.xMin = foldoutLabelNameRect.xMax;
			foldoutInfoRect.width = italicLabel.CalcSize(infoText).x;
			foldoutFieldRect.xMin = foldoutInfoRect.xMax + 4f;
			foldoutFieldRect.width = 50f;
			//now fix anything that overflows!
			var xMax = position.xMax;
			if (foldoutFieldRect.xMax > xMax)
			{
				foldoutFieldRect.xMin = xMax - 50f;
				foldoutInfoRect.width = xMax - labelWidth - 50f - 15f - 4f - 4f - 6f - 6f;
			}
			foldoutFieldRect.width = 40f;
			float fieldValue = dnaNameForWeightProp.stringValue == "" ? globalWeightProp.floatValue : 0.5f;
			//I want to evaluate the dna so users can see how different evaluators affect the value
			if (!string.IsNullOrEmpty(dnaNameForWeightProp.stringValue) && Application.isPlaying)
			{
				//this really has to get the current dna value else its too confusing
				//fieldValue = EvalauateValue(fieldValue, dnaForWeightProp);
			}
			EditorGUI.BeginDisabledGroup(true);
			EditorGUI.LabelField(foldoutInfoRect, infoText, italicLabel);
			//Dont do a disabled field because its too confusing
			//EditorGUI.FloatField(foldoutFieldRect, fieldValue);
			if (string.IsNullOrEmpty(dnaNameForWeightProp.stringValue))
			{
				EditorGUI.LabelField(foldoutFieldRect, "[" + fieldValue.ToString("0.00") + "]");
			}
			EditorGUI.EndDisabledGroup();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			var height = EditorGUIUtility.singleLineHeight;
			if (property.isExpanded)
			{
				height += (EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2)) * 3f;
				//dnaEvaluator is a bit higher than this
				height += 8f;
			}
			return height;
		}

		//I'd really like this if its inspected at runtime to show the actual DNA value thats chosen being evaluated
		//but for that I'd need some kind of context thing like UMABonePose has
		//I'll get to it...TODO
		protected float EvalauateValue(float value, SerializedProperty evaluatorProp)
		{
			//maybe I can get the active dna off the plugin using property.serializedObject.targetObject like dnaEvalutaor does?
			var multiplierProp = evaluatorProp.FindPropertyRelative("_multiplier");
			var graphProp = evaluatorProp.FindPropertyRelative("_evaluator");
			var graphNameProp = graphProp.FindPropertyRelative("_name");
			var graphCurveProp = graphProp.FindPropertyRelative("_graph");
			dummyEvaluator = new DNAEvaluationGraph(graphNameProp.stringValue, graphCurveProp.animationCurveValue);
			return (dummyEvaluator.Evaluate(value) * multiplierProp.floatValue);
		}

	}
}