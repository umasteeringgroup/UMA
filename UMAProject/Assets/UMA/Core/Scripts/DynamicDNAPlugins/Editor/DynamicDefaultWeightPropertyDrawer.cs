using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomPropertyDrawer(typeof(DynamicDefaultWeight), true)]
	public class DynamicDefaultWeightPropertyDrawer : PropertyDrawer
	{
		protected DNAEvaluationGraph dummyEvaluator = new DNAEvaluationGraph();
		protected GUIStyle italicLabel;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			var prevIndentlevel = EditorGUI.indentLevel;
			position = EditorGUI.IndentedRect(position);
			EditorGUI.indentLevel = 0;
			if (italicLabel == null)
			{
				italicLabel = new GUIStyle(EditorStyles.label);
				italicLabel.clipping = TextClipping.Clip;
				italicLabel.fontStyle = FontStyle.Italic;
			}
			
			var foldoutRect = new Rect(position.xMin, position.yMin, position.width - 6f, EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2));
			EditorGUI.PropertyField(foldoutRect, property, label, true);

			DrawCurrentSettingInfo(property, foldoutRect, position);

			EditorGUI.indentLevel = prevIndentlevel;
		}

		protected void DrawCurrentSettingInfo(SerializedProperty property, Rect foldoutRect, Rect position)
		{
			var foldoutLabelNameRect = new Rect(foldoutRect.xMin + 9f, foldoutRect.yMin, 70f, foldoutRect.height);
			var foldoutInfoRect = new Rect(foldoutLabelNameRect.xMax + 4f, foldoutRect.yMin, 170f, foldoutRect.height);
			var foldoutFieldRect = new Rect(foldoutInfoRect.xMax + 4f, foldoutRect.yMin, 40f, EditorGUIUtility.singleLineHeight);

			var defaultWeightProp = property.FindPropertyRelative("_defaultWeight");
			var dnaForWeightProp = property.FindPropertyRelative("_DNAForWeight");
			var dnaNameForWeightProp = dnaForWeightProp.FindPropertyRelative("_dnaName");
			var onMissingDNAProp = property.FindPropertyRelative("_onMissingDNA");

			GUIContent infoText = dnaNameForWeightProp.stringValue == "" ? new GUIContent("Using Default Weight : ") : new GUIContent("Using DNA name '" + dnaNameForWeightProp.stringValue + "' : ");

			//recalc widths based on that
			var labelWidth = (EditorStyles.foldout.CalcSize(new GUIContent(property.displayName)).x - 15f);
			foldoutLabelNameRect.width = labelWidth;
			foldoutInfoRect.xMin = foldoutLabelNameRect.xMax;
			foldoutInfoRect.width = italicLabel.CalcSize(infoText).x;
			foldoutFieldRect.xMin = foldoutInfoRect.xMax + 4f;
			foldoutFieldRect.width = 40f;
			//now fix anything that overflows!
			var xMax = position.xMax;
			if (foldoutFieldRect.xMax > xMax)
			{
				foldoutFieldRect.xMin = xMax - 40f;
				foldoutInfoRect.width = xMax - labelWidth - 40f - 15f - 4f - 4f - 6f - 6f;
			}
			foldoutFieldRect.width = 40f;
			float fieldValue = dnaNameForWeightProp.stringValue == "" ? defaultWeightProp.floatValue : 0.5f;
			//I want to evaluate the dna so users can see how different evaluators affect the value
			if (dnaNameForWeightProp.stringValue != "")
			{
				fieldValue = EvalauateValue(fieldValue, dnaForWeightProp);
			}
			EditorGUI.BeginDisabledGroup(true);
			EditorGUI.LabelField(foldoutInfoRect, infoText, italicLabel);
			EditorGUI.FloatField(foldoutFieldRect, fieldValue);
			EditorGUI.EndDisabledGroup();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			var height = EditorGUIUtility.singleLineHeight;
			if (property.isExpanded)
			{
				height += (EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2)) * 4f;
				//dnaEvaluator is a bit higher than this
				height += 6f;
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
