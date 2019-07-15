using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UMA;
using UnityEditorInternal;

namespace UMA.Editors
{
	[CustomPropertyDrawer(typeof(DNAEvaluator), true)]
	public class DNAEvaluatorPropertyDrawer : PropertyDrawer
	{
		private const string CALCOPTIONPROPERTY = "_calcOption";
		private const string DNANAMEPROPERTY = "_dnaName";
		private const string DNANAMEHASHPROPERTY = "_dnaNameHash";
		private const string EVALUATORPROPERTY = "_evaluator";
		private const string MULTIPLIERPROPERTY = "_multiplier";

		private const string DNANAMELABEL = "DNA Name";
		private const string EVALUATORLABEL = "Evaluator";
		private const string MULTIPLIERLABEL = "Multiplier";
		private const string CALCOPTIONMINILABEL = "\u01A9";

		private DNAEvaluator _target;

		private bool _drawLabels = true;
		private bool _alwaysExpanded = false;
		private bool _drawCalcOption = false;

		private float _calcOptionWidth = 25f;
		private GUIContent _calcOptionHeaderLabel = new GUIContent("\u03A3", "Define how the evaluated value will be combined with the previous Evaluator in the list.");
		private GUIContent[] _calcOptionMiniLabels = new GUIContent[] { new GUIContent("+", "Add"), new GUIContent("-", "Subtract"), new GUIContent("\u0078", "Multiply"), new GUIContent("\u00F7", "Divide") };

		private float _multiplierLabelWidth = 55f;
		private Vector2 _dnaToEvaluatorRatio = new Vector2(2f, 3f);
		private float _padding = 2f;
		private bool _manuallyConfigured = false;

		//if this is drawn in a DynamicDNAPlugin it should give us dna names to choose from
		private DynamicDNAPlugin _dynamicDNAPlugin;

		private bool initialized = false;

		public bool DrawLabels
		{
			set
			{
				_drawLabels = value;
				_manuallyConfigured = true;
			}
		}

		public bool AlwaysExpanded
		{
			set
			{
				_alwaysExpanded = value;
				_manuallyConfigured = true;
			}
		}

		public bool DrawCalcOption
		{
			set
			{
				_drawCalcOption = value;
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
					var attrib = this.fieldInfo.GetCustomAttributes(typeof(DNAEvaluator.ConfigAttribute), true).FirstOrDefault() as DNAEvaluator.ConfigAttribute;
					if (attrib != null)
					{
						_drawLabels = attrib.drawLabels;
						_alwaysExpanded = attrib.alwaysExpanded;
						_drawCalcOption = attrib.drawCalcOption;
					}
				}
			}
			initialized = true;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			Event current = Event.current;
			label = EditorGUI.BeginProperty(position, label, property);

			//Try and get a DNAAsset from the serializedObject- this is used for showing a popup in the dna field rather than a text field
			CheckDynamicDNAPlugin(property);

			Init();

			if (!_alwaysExpanded)
			{
				var foldoutPos = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
				property.isExpanded = EditorGUI.Foldout(foldoutPos, property.isExpanded, label);
			}
			var reorderableListDefaults = new ReorderableList.Defaults();

			if (property.isExpanded || _alwaysExpanded)
			{
				EditorGUI.indentLevel++;
				position = EditorGUI.IndentedRect(position);
				if (!_alwaysExpanded)
					position.yMin = position.yMin + EditorGUIUtility.singleLineHeight;
				else
					position.yMin += 2f;
				position.xMin -= 15f;//make it the same width as a reorderable list
				if (_drawLabels)
				{
					//can we draw this so it looks like the header of a reorderable List?
					if (current.type == EventType.Repaint)
						reorderableListDefaults.headerBackground.Draw(position, GUIContent.none, false, false, false, false);
					var rect1 = new Rect(position.xMin + 6f, position.yMin + 1f, position.width - 12f, position.height);
					if (_alwaysExpanded)
						position = DoLabelsInline(rect1, label);
					else
						position = DoLabelsInline(rect1, DNANAMELABEL);
					position.xMin -= 6f;
					position.width += 6f;
					position.yMin -= 1f;
					position.height -= 3f;
				}
				if (current.type == EventType.Repaint)
					reorderableListDefaults.boxBackground.Draw(position, GUIContent.none, false, false, false, false);
				var rect2 = new Rect(position.xMin + 6f, position.yMin + 3f, position.width - 12f, position.height);
				DoFieldsInline(rect2, property);
				EditorGUI.indentLevel--;
			}

			EditorGUI.EndProperty();
		}

		public Rect DoLabelsInline(Rect position, string label1 = DNANAMELABEL, string label2 = EVALUATORLABEL, string label3 = MULTIPLIERLABEL)
		{
			return DoLabelsInline(position, new GUIContent(label1, GetChildTooltip(DNANAMEPROPERTY)), new GUIContent(label2, GetChildTooltip(EVALUATORPROPERTY)), new GUIContent(label3, GetChildTooltip(MULTIPLIERPROPERTY)));
		}

		public Rect DoLabelsInline(Rect position, GUIContent label1, GUIContent label2 = null, GUIContent label3 = null)
		{
			if (label2 == null)
				label2 = new GUIContent(EVALUATORLABEL, GetChildTooltip(EVALUATORPROPERTY));
			if (label3 == null)
				label3 = new GUIContent(MULTIPLIERLABEL, GetChildTooltip(MULTIPLIERPROPERTY));

			var prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			Rect calcOptionRect = Rect.zero;
			if (_drawCalcOption)
			{
				calcOptionRect = new Rect(position.xMin+15f, position.yMin, _calcOptionWidth, EditorGUIUtility.singleLineHeight);
				position.xMin = calcOptionRect.xMax;
			}
			var fieldbaseRatio = (position.width - _multiplierLabelWidth) / (_dnaToEvaluatorRatio.x + _dnaToEvaluatorRatio.y);
			var dnafieldWidth = fieldbaseRatio * _dnaToEvaluatorRatio.x;
			var evaluatorFieldWidth = fieldbaseRatio * _dnaToEvaluatorRatio.y;

			var dnaNameLabelRect = new Rect(position.xMin, position.yMin, dnafieldWidth, EditorGUIUtility.singleLineHeight);
			var evaluatorLabelRect = new Rect(dnaNameLabelRect.xMax, position.yMin, evaluatorFieldWidth, EditorGUIUtility.singleLineHeight);
			var multiplierLabelRect = new Rect(evaluatorLabelRect.xMax, position.yMin, _multiplierLabelWidth, EditorGUIUtility.singleLineHeight);
			if (_drawCalcOption)
			{
				EditorGUI.LabelField(calcOptionRect, _calcOptionHeaderLabel, EditorStyles.centeredGreyMiniLabel);
			}
			EditorGUI.LabelField(dnaNameLabelRect, label1, EditorStyles.centeredGreyMiniLabel);
			EditorGUI.LabelField(evaluatorLabelRect, label2, EditorStyles.centeredGreyMiniLabel);
			EditorGUI.LabelField(multiplierLabelRect, label3, EditorStyles.centeredGreyMiniLabel);
			position.yMin = dnaNameLabelRect.yMax + 2f;

			EditorGUI.indentLevel = prevIndent;

			return position;
		}

		/// <summary>
		/// Draws a DNAEvaluator with inline styling
		/// </summary>
		/// <param name="position"></param>
		/// <param name="property"></param>
		/// <param name="label"></param>
		public void DoFieldsInline(Rect position, SerializedProperty property)
		{
			CheckDynamicDNAPlugin(property);

			Init();

			var prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			Rect calcOptionRect = Rect.zero;
			if (_drawCalcOption)
			{
				calcOptionRect = new Rect(position.xMin, position.yMin, _calcOptionWidth, EditorGUIUtility.singleLineHeight);
				position.xMin = calcOptionRect.xMax;
			}
			var calcOptionProp = property.FindPropertyRelative(CALCOPTIONPROPERTY);
			var dnaNameProp = property.FindPropertyRelative(DNANAMEPROPERTY);
			var dnaNameHashProp = property.FindPropertyRelative(DNANAMEHASHPROPERTY);
			var evaluatorProp = property.FindPropertyRelative(EVALUATORPROPERTY);
			var intensityProp = property.FindPropertyRelative(MULTIPLIERPROPERTY);
			var fieldbaseRatio = (position.width - _multiplierLabelWidth) / (_dnaToEvaluatorRatio.x + _dnaToEvaluatorRatio.y);
			var dnafieldWidth = fieldbaseRatio * _dnaToEvaluatorRatio.x;
			var evaluatorFieldWidth = fieldbaseRatio * _dnaToEvaluatorRatio.y;

			position.height = EditorGUIUtility.singleLineHeight;//theres a space at the bottom so cut that off

			var dnaNameRect = new Rect(position.xMin + _padding, position.yMin, dnafieldWidth - (_padding * 2), position.height);
			var evaluatorRect = new Rect(dnaNameRect.xMax + (_padding * 2), position.yMin, evaluatorFieldWidth - (_padding * 2), position.height);
			var multiplierRect = new Rect(evaluatorRect.xMax + (_padding * 2), position.yMin, _multiplierLabelWidth - (_padding * 2), position.height);

			if (_drawCalcOption)
			{
				calcOptionProp.enumValueIndex = EditorGUI.Popup(calcOptionRect, calcOptionProp.enumValueIndex, _calcOptionMiniLabels);
			}
			EditorGUI.BeginChangeCheck();
			if (_dynamicDNAPlugin == null)
				EditorGUI.PropertyField(dnaNameRect, dnaNameProp, GUIContent.none);
			else
			{
				DynamicDNAConverterControllerInspector.DNANamesPopup(dnaNameRect, dnaNameProp, dnaNameProp.stringValue, _dynamicDNAPlugin.converterController.DNAAsset);
			}
			if (EditorGUI.EndChangeCheck())
			{
				if (!string.IsNullOrEmpty(dnaNameProp.stringValue))
					dnaNameHashProp.intValue = UMAUtils.StringToHash(dnaNameProp.stringValue);
				else
					dnaNameHashProp.intValue = -1;
			}
			EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(dnaNameProp.stringValue));
			EditorGUI.BeginChangeCheck();
			EditorGUI.PropertyField(evaluatorRect, evaluatorProp, GUIContent.none);
			if (EditorGUI.EndChangeCheck())
			{
				InspectorUtlity.RepaintAllInspectors();
				GUI.changed = true;
			}
			EditorGUI.PropertyField(multiplierRect, intensityProp, GUIContent.none);
			EditorGUI.EndDisabledGroup();

			EditorGUI.indentLevel = prevIndent;
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (!_manuallyConfigured)
			{
				if (this.fieldInfo != null)
				{
					var attrib = this.fieldInfo.GetCustomAttributes(typeof(DNAEvaluator.ConfigAttribute), true).FirstOrDefault() as DNAEvaluator.ConfigAttribute;
					if (attrib != null)
					{
						_drawLabels = attrib.drawLabels;
						_alwaysExpanded = attrib.alwaysExpanded;
						_drawCalcOption = attrib.drawCalcOption;
					}

				}
			}
			if (property.isExpanded || _alwaysExpanded)
			{
				if (_drawLabels)
					return EditorGUIUtility.singleLineHeight * (_alwaysExpanded ? 2f : 3f) + (_padding * 4f) + 6f;
				else
					return EditorGUIUtility.singleLineHeight * (_alwaysExpanded ? 1f : 2f) + (_padding * 4f);
			}
			else
				return EditorGUI.GetPropertyHeight(property, true);
		}

		/// <summary>
		/// if this property is in a DynamicDNAPlugin this will find it so we can use its DNAAsset etc
		/// </summary>
		/// <param name="property"></param>
		private void CheckDynamicDNAPlugin(SerializedProperty property)
		{
			if (typeof(DynamicDNAPlugin).IsAssignableFrom((property.serializedObject.targetObject).GetType()))
			{
				_dynamicDNAPlugin = (DynamicDNAPlugin)property.serializedObject.targetObject;
			}
		}

		/// <summary>
		/// Gets the tooltip from the 'Tooltip' attribute defined in the type class (if set)
		/// </summary>
		private string GetChildTooltip(SerializedProperty property)
		{

			return GetChildTooltip(property.name);
		}
		/// <summary>
		/// Gets the tooltip from the 'Tooltip' attribute defined in the type class (if set)
		/// </summary>
		private string GetChildTooltip(string propertyName)
		{

			TooltipAttribute[] attributes = typeof(DNAEvaluator).GetField(propertyName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetCustomAttributes(typeof(TooltipAttribute), true) as TooltipAttribute[];

			return attributes.Length > 0 ? attributes[0].tooltip : "";
		}

	}
}
