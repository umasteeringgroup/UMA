using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace UMA.Editors
{
	[CustomPropertyDrawer(typeof(DNAEvaluatorList), true)]
	public class DNAEvaluatorListPropertyDrawer : PropertyDrawer
	{
		private SerializedProperty _property;

		private float _padding = 2f;

		private ReorderableList _dnaEvaluatorList;

		private DNAEvaluatorPropertyDrawer _dnaEvaluatorDrawer = new DNAEvaluatorPropertyDrawer();

		private bool _drawAsReorderableList = true;

		private GUIStyle _aggregationLabelStyle;

		ReorderableList.Defaults ROLDefaults;

		public bool DrawAsReorderableList
		{
			get { return _drawAsReorderableList; }
			set { _drawAsReorderableList = value; }
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (property.isExpanded)
			{
				if (_drawAsReorderableList)
				{
					var dnaEvalListProp = property.FindPropertyRelative("_dnaEvaluators");
					var h = (EditorGUIUtility.singleLineHeight + (_padding * 3)) * 3;
					if (dnaEvalListProp.arraySize > 0)
					{
						for (int i = 0; i < dnaEvalListProp.arraySize; i++)
							h += EditorGUIUtility.singleLineHeight + (_padding * 2);
					}
					else
					{
						h += EditorGUIUtility.singleLineHeight + (_padding * 2);
					}
					return h;
				}
				else
				{
					return EditorGUI.GetPropertyHeight(property, true) - EditorGUIUtility.singleLineHeight;
				}
			}
			else
			{
				return EditorGUI.GetPropertyHeight(property, true);
			}
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			
			label = EditorGUI.BeginProperty(position, label, property);

			_property = property;

			_aggregationLabelStyle = new GUIStyle(EditorStyles.label);

			if(ROLDefaults == null)
				ROLDefaults = new ReorderableList.Defaults();

			if (!_drawAsReorderableList)
				DrawDefaultList(position, property, label);
			else
			{
				_aggregationLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
				_aggregationLabelStyle.alignment = TextAnchor.MiddleLeft;
				DrawReorderableList(position, property, label);
			}

			EditorGUI.EndProperty();
		}

		private void DrawReorderableList(Rect position, SerializedProperty property, GUIContent label)
		{
			var foldoutRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label);
			if (property.isExpanded)
			{
				var contentRect = EditorGUI.IndentedRect(position);
				contentRect.yMin = foldoutRect.yMax + _padding;
				contentRect.height = contentRect.height - foldoutRect.height;

				var dnaEvalListProp = property.FindPropertyRelative("_dnaEvaluators");
				_dnaEvaluatorList = CachedReorderableList.GetListDrawer(dnaEvalListProp, DrawHeaderCallback, null, DrawElementCallback, DrawFooterCallback);

				_dnaEvaluatorList.DoList(contentRect);
			}
		}

		private void DrawHeaderCallback(Rect rect)
		{
			_dnaEvaluatorDrawer.DrawInline = true;
			_dnaEvaluatorDrawer.DrawLabels = false;
			var dragHandleSize = 30f;
			var labelsRect = new Rect(rect.xMin, rect.yMin, rect.width - dragHandleSize, rect.height);
			_dnaEvaluatorDrawer.DoLabelsInline(rect);
		}

		private void DrawFooterCallback(Rect rect)
		{
			float xMax = rect.xMax;
			float availWidth = xMax - 60f;
			//if the view is wide this only needs to be XMax - 300 else its Xmin
			float bgXmin = rect.xMax - 300f > rect.xMin ? rect.xMax - 300f : rect.xMin;
			Rect bgRect = new Rect(bgXmin, rect.yMin, rect.width, rect.height);
			bgRect.xMax = rect.xMax;
			rect = new Rect(availWidth, rect.y, xMax - availWidth -4f, rect.height);
			Rect rect2 = new Rect(availWidth + 4f, rect.y - 3f, 25f, 13f);
			Rect rect3 = new Rect(xMax - 33f, rect.y - 3f, 25f, 13f);
			Rect agRect = new Rect(bgRect.xMin + 8f, bgRect.yMin, (bgRect.width - rect.width) -16f, bgRect.height);
			if (Event.current.type == EventType.Repaint)
			{
				var prevFooterFixedHeight = ROLDefaults.footerBackground.fixedHeight;
				//usually 13f but we need it higher to hold aggregation controls
				ROLDefaults.footerBackground.fixedHeight = 13f + (EditorGUIUtility.standardVerticalSpacing * 3);
				ROLDefaults.footerBackground.Draw(bgRect, false, false, false, false);
				//now draw the standard background for the +/- controls
				ROLDefaults.footerBackground.fixedHeight = prevFooterFixedHeight + 2f;
				ROLDefaults.footerBackground.Draw(rect, false, false, false, false);
				ROLDefaults.footerBackground.fixedHeight = prevFooterFixedHeight;
			}
			if (GUI.Button(rect2, ROLDefaults.iconToolbarPlus, ROLDefaults.preButton))
			{
				ROLDefaults.DoAddButton(_dnaEvaluatorList);
			}
			using (new EditorGUI.DisabledScope(_dnaEvaluatorList.index < 0 || _dnaEvaluatorList.index >= _dnaEvaluatorList.count))
			{
				if (GUI.Button(rect3, ROLDefaults.iconToolbarMinus, ROLDefaults.preButton))
				{
					ROLDefaults.DoRemoveButton(_dnaEvaluatorList);
				}
			}
			var prevIndentLevel = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;
			DrawAggregationMethod(agRect, _property);
			EditorGUI.indentLevel = prevIndentLevel;
		}

		private void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
		{
			_dnaEvaluatorDrawer.DrawInline = true;
			_dnaEvaluatorDrawer.DrawLabels = false;
			var dnaEvalListProp = _dnaEvaluatorList.serializedProperty;
			var entryRect = new Rect(rect.xMin, rect.yMin + _padding, rect.width, rect.height - _padding);
			_dnaEvaluatorDrawer.DoFieldsInline(entryRect, dnaEvalListProp.GetArrayElementAtIndex(index));
		}

		/// <summary>
		/// Draws the list in the default Unity style (but with only one foldout rather than one for the property and one for the list)
		/// </summary>
		private void DrawDefaultList(Rect position, SerializedProperty property, GUIContent label)
		{
			var dnaEvalListProp = property.FindPropertyRelative("_dnaEvaluators");

			var foldoutRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label);
			if (property.isExpanded)
			{
				EditorGUI.indentLevel--;
				var contentRect = EditorGUI.IndentedRect(position);//this is too indented, why?
				EditorGUI.indentLevel++;
				contentRect.yMin = foldoutRect.yMax + _padding;
				contentRect.height = contentRect.height - foldoutRect.height;

				//do the aggregation method
				contentRect = DrawAggregationMethod(contentRect, property);

				var sizeRect = new Rect(contentRect.xMin, contentRect.yMin, contentRect.width, EditorGUIUtility.singleLineHeight);

				//the label is in the right place but the field is too indented compared to a standard list drawer so
				var prevLabelWidth = EditorGUIUtility.labelWidth;
				EditorGUIUtility.labelWidth = EditorGUIUtility.labelWidth - 15f;

				dnaEvalListProp.arraySize = EditorGUI.IntField(sizeRect, "Size", dnaEvalListProp.arraySize);

				var entryRect = new Rect(contentRect.xMin, sizeRect.yMax + _padding, contentRect.width, EditorGUIUtility.singleLineHeight);
				for (int i = 0; i < dnaEvalListProp.arraySize; i++)
				{
					var dnaEvalProp = dnaEvalListProp.GetArrayElementAtIndex(i);
					entryRect.height = EditorGUI.GetPropertyHeight(dnaEvalProp);
					EditorGUI.PropertyField(entryRect, dnaEvalProp, true);
					entryRect.yMin = entryRect.yMax + _padding;
				}
				EditorGUIUtility.labelWidth = prevLabelWidth;
			}
		}

		private Rect DrawAggregationMethod(Rect position, SerializedProperty property)
		{
			var labelRectMinWidth = 110f;
			var popupMinWidth = 60f;
			var aggregationProp = property.FindPropertyRelative("_aggregationMethod");
			var aggregationRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
			var labelRectWidth = aggregationRect.width - labelRectMinWidth < popupMinWidth ? aggregationRect.width / 2f : labelRectMinWidth;
			var popupRectWidth = aggregationRect.width - labelRectMinWidth < popupMinWidth ? aggregationRect.width / 2f : aggregationRect.width - labelRectWidth;
			var labelRect = new Rect(aggregationRect.xMin, aggregationRect.yMin, labelRectWidth, aggregationRect.height);
			var popupRect = new Rect(labelRect.xMax, aggregationRect.yMin, popupRectWidth, aggregationRect.height);
			EditorGUI.LabelField(labelRect, aggregationProp.displayName, _aggregationLabelStyle);
			EditorGUI.PropertyField(popupRect, aggregationProp, GUIContent.none);
			var retRect = new Rect(position.xMin, aggregationRect.yMax + _padding, position.width, position.height - (EditorGUIUtility.singleLineHeight + _padding));
			return retRect;
		}
	}
}
