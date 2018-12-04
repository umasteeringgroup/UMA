using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace UMA.Editors
{
	[CustomPropertyDrawer(typeof(DNAEvaluatorList), true)]
	public class DNAEvaluatorListPropertyDrawer : PropertyDrawer
	{

		private const string DNAEVALUATORSPROPERTY = "_dnaEvaluators";
		private const string AGGREGATIONMETHODPROPERTY = "_aggregationMethod";

		private SerializedProperty _property;

		private GUIContent _propertyLabel;

		private float _padding = EditorGUIUtility.standardVerticalSpacing;

		private ReorderableList _dnaEvaluatorList;

		private DNAEvaluatorPropertyDrawer _dnaEvaluatorDrawer = new DNAEvaluatorPropertyDrawer();

		//private bool _drawAsReorderableList = true;

		private DNAEvaluatorList.ConfigAttribute.LabelOptions _labelOption = DNAEvaluatorList.ConfigAttribute.LabelOptions.drawLabelAsFoldout;

		private DNAEvaluationGraph _defaultGraph = null;

		private bool _manuallyConfigured = false;

		private GUIStyle _aggregationLabelStyle;

		ReorderableList.Defaults ROLDefaults;

		bool initialized = false;

		//If the list aggregationMode is 'Cumulative' draw the calc options
		private bool drawCalcOption = false;

		public DNAEvaluatorList.ConfigAttribute.LabelOptions LabelOption
		{
			get { return _labelOption; }
			set {
				_labelOption = value;
				_manuallyConfigured = true;
			}
		}
		//TODO Impliment this
		/// <summary>
		/// Not Implimented
		/// </summary>
		public DNAEvaluationGraph DefaultGraph
		{
			get { return _defaultGraph; }
			set {
				_defaultGraph = value;
				if (_defaultGraph != null)
					_manuallyConfigured = true;
			}
		}

		private void Init()
		{
			if (initialized)
				return;

			if (!_manuallyConfigured)
			{
				if (this.fieldInfo != null)
				{
					var attrib = this.fieldInfo.GetCustomAttributes(typeof(DNAEvaluatorList.ConfigAttribute), true).FirstOrDefault() as DNAEvaluatorList.ConfigAttribute;
					if (attrib != null)
					{
						_labelOption = attrib.labelOption;
						_defaultGraph = attrib.defaultGraph;
					}
				}
			}
			_aggregationLabelStyle = new GUIStyle(EditorStyles.label);
			_aggregationLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
			_aggregationLabelStyle.alignment = TextAnchor.MiddleLeft;

			if (ROLDefaults == null)
				ROLDefaults = new ReorderableList.Defaults();

			initialized = true;

		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			Init();

			if (property.isExpanded 
				|| _labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedNoLabel 
				|| _labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedWithLabel)
			{
				var dnaEvalListProp = property.FindPropertyRelative(DNAEVALUATORSPROPERTY);
				float h = 0f;

				if(_labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedWithLabel 
					|| _labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawLabelAsFoldout)
					h = (EditorGUIUtility.singleLineHeight + (_padding)) * 3;

				if (dnaEvalListProp.arraySize > 0)
				{
					//we only show the aggregation method if there is more than one and that makes the footer higher
					if(dnaEvalListProp.arraySize > 1)
					{
						h += _padding * 3;
					}
					for (int i = 0; i < dnaEvalListProp.arraySize; i++)
						h += EditorGUIUtility.singleLineHeight + (_padding * 2)+1f;
				}
				else
				{
					h += EditorGUIUtility.singleLineHeight + (_padding);
				}
				return h;
			}
			else
			{
				return EditorGUI.GetPropertyHeight(property, true);
			}
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{

			_propertyLabel = label = EditorGUI.BeginProperty(position, label, property);

			_property = property;

			Init();

			var aggregationProp = property.FindPropertyRelative(AGGREGATIONMETHODPROPERTY);
			if (aggregationProp.enumValueIndex == 1)//Cumulative
				drawCalcOption = true;
			else
				drawCalcOption = false;

			DrawReorderableList(position, property, label);

			EditorGUI.EndProperty();
		}

		private void DrawReorderableList(Rect position, SerializedProperty property, GUIContent label)
		{
			var labelRect = new Rect(position.xMin, position.yMin, position.width, 0f);
			if (_labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawLabelAsFoldout)
			{
				labelRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
				property.isExpanded = EditorGUI.Foldout(labelRect, property.isExpanded, label);
			}
			else if(_labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedWithLabel)
			{
				labelRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
				EditorGUI.LabelField(labelRect, label);
			}
			if (property.isExpanded || _labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedWithLabel || _labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedNoLabel)
			{
				var contentRect = EditorGUI.IndentedRect(position);
				contentRect.yMin = labelRect.yMax + _padding;
				contentRect.height = contentRect.height - labelRect.height;

				var dnaEvalListProp = property.FindPropertyRelative(DNAEVALUATORSPROPERTY);
				
				_dnaEvaluatorList = CachedReorderableList.GetListDrawer(dnaEvalListProp, DrawHeaderCallback, null, DrawElementCallback, DrawFooterCallback);

				_dnaEvaluatorList.DoList(contentRect);
			}
		}

		private void DrawHeaderCallback(Rect rect)
		{
			_dnaEvaluatorDrawer.DrawLabels = false;
			_dnaEvaluatorDrawer.DrawCalcOption = _dnaEvaluatorList.count > 1 ? drawCalcOption : false;
			if (_labelOption == DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedNoLabel)
			{
				_dnaEvaluatorDrawer.DoLabelsInline(rect, _propertyLabel);
			}
			else
			{
				_dnaEvaluatorDrawer.DoLabelsInline(rect);
			}
		}

		private void DrawFooterCallback(Rect rect)
		{
			float xMax = rect.xMax;
			float availWidth = xMax - 60f;
			//if the view is wide this only needs to be XMax - 300 else its Xmin
			float bgXmin = rect.xMax - 300f > rect.xMin ? rect.xMax - 300f : rect.xMin;
			Rect bgRect = new Rect(bgXmin, rect.yMin, rect.width, rect.height);
			bgRect.xMax = rect.xMax;
			float agWidthMod = 0f;
			//If the list count is greater than 1 we need to draw the background for the aggregation controls
			if (_dnaEvaluatorList.count > 1)
			{
				agWidthMod = 4f;
			}
			rect = new Rect(availWidth, rect.y, xMax - availWidth - agWidthMod, rect.height);
			Rect rect2 = new Rect(availWidth + 4f, rect.y - 3f, 25f, 13f);
			Rect rect3 = new Rect(xMax - 33f, rect.y - 3f, 25f, 13f);
			Rect agRect = new Rect(bgRect.xMin + 8f, bgRect.yMin, (bgRect.width - rect.width) -16f, bgRect.height);
			if (Event.current.type == EventType.Repaint)
			{
				var prevFooterFixedHeight = ROLDefaults.footerBackground.fixedHeight;
				var addMinusHeight = prevFooterFixedHeight;
				//If the list count is greater than 1 we need to draw the background for the aggregation controls
				if (_dnaEvaluatorList.count > 1)
				{
					//usually 13f but we need it higher to hold aggregation controls
					ROLDefaults.footerBackground.fixedHeight = 13f + (EditorGUIUtility.standardVerticalSpacing * 3);
					ROLDefaults.footerBackground.Draw(bgRect, false, false, false, false);
					addMinusHeight += 2f;
				}
				//now draw the standard background for the +/- controls
				ROLDefaults.footerBackground.fixedHeight = addMinusHeight;
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
			//If the list count is greater than 1 we need to draw the background for the aggregation controls
			if (_dnaEvaluatorList.count > 1)
			{
				DrawAggregationMethod(agRect, _property);
			}
			EditorGUI.indentLevel = prevIndentLevel;
		}

		//GetArrayElementAtIndex is slow, we need to cache the results the same way DNAPluginsDrawerer does
		private void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
		{
			_dnaEvaluatorDrawer.DrawLabels = false;
			var dnaEvalListProp = _dnaEvaluatorList.serializedProperty;
			var entryRect = new Rect(rect.xMin, rect.yMin + _padding, rect.width, rect.height );
			_dnaEvaluatorDrawer.DrawCalcOption = _dnaEvaluatorList.count > 1 ? drawCalcOption : false;
			_dnaEvaluatorDrawer.DoFieldsInline(entryRect, dnaEvalListProp.GetArrayElementAtIndex(index));
		}

		private Rect DrawAggregationMethod(Rect position, SerializedProperty property)
		{
			var labelRectMinWidth = 110f;
			var popupMinWidth = 60f;
			var aggregationProp = property.FindPropertyRelative(AGGREGATIONMETHODPROPERTY);
			var aggregationRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
			var labelRectWidth = aggregationRect.width - labelRectMinWidth < popupMinWidth ? aggregationRect.width / 2f : labelRectMinWidth;
			var popupRectWidth = aggregationRect.width - labelRectMinWidth < popupMinWidth ? aggregationRect.width / 2f : aggregationRect.width - labelRectWidth;
			var labelRect = new Rect(aggregationRect.xMin, aggregationRect.yMin, labelRectWidth, aggregationRect.height);
			var popupRect = new Rect(labelRect.xMax, aggregationRect.yMin, popupRectWidth, aggregationRect.height);

			var label = EditorGUI.BeginProperty(labelRect, new GUIContent(aggregationProp.displayName), aggregationProp);
			EditorGUI.LabelField(labelRect, label, _aggregationLabelStyle);
			EditorGUI.PropertyField(popupRect, aggregationProp, GUIContent.none);
			EditorGUI.EndProperty();

			var retRect = new Rect(position.xMin, aggregationRect.yMax + _padding, position.width, position.height - (EditorGUIUtility.singleLineHeight + _padding));
			return retRect;
		}
	}
}
