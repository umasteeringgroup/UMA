using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA
{
	//this is literally just to put two extra pixels height in for a MasterWeight field- yeah I'm OCD'ing here
	[CustomPropertyDrawer(typeof(DynamicDNAPlugin.MasterWeight),true)]
	public class DynamicDNAPluginMasterWeightDrawer : DynamicDefaultWeightPropertyDrawer
	{
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
				var defaultWeightProp = property.FindPropertyRelative("_defaultWeight");
				var dnaForWeightProp = property.FindPropertyRelative("_DNAForWeight");
				var onMissingDNAProp = property.FindPropertyRelative("_onMissingDNA");
				var rect = EditorGUI.IndentedRect(foldoutRect);
				var field1Rect = new Rect(rect.xMin + 10f, foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing, rect.width -10f, EditorGUI.GetPropertyHeight(defaultWeightProp));
				var field2Rect = new Rect(field1Rect.xMin, field1Rect.yMax, field1Rect.width, EditorGUI.GetPropertyHeight(dnaForWeightProp));
				var field3Rect = new Rect(field1Rect.xMin, field2Rect.yMax, field1Rect.width, EditorGUI.GetPropertyHeight(onMissingDNAProp));
				EditorGUI.PropertyField(field1Rect, defaultWeightProp);
				EditorGUI.PropertyField(field2Rect, dnaForWeightProp);
				EditorGUI.PropertyField(field3Rect, onMissingDNAProp);
			}

			DrawCurrentSettingInfo(property, foldoutRect, position);

		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			var height = EditorGUIUtility.singleLineHeight;
			if (property.isExpanded)
			{
				height += (EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2)) * 4f;
				//dnaEvaluator is a bit higher than this
				height += 8f;
			}
			return height;
		}

	}
}
