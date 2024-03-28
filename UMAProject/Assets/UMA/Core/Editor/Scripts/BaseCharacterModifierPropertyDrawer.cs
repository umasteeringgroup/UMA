using System.Linq;
using UnityEngine;
using UnityEditor;

namespace UMA
{
    [CustomPropertyDrawer(typeof(BaseCharacterModifier),true)]
	public class BaseCharacterModifierPropertyDrawer : PropertyDrawer
	{
		BaseCharacterModifier _target;

		private bool _alwaysExpanded = false;
		private bool _manuallyConfigured = false;

		bool initialized = false;

		public bool AlwaysExpanded
		{
			set
			{
				_alwaysExpanded = value;
				_manuallyConfigured = true;
			}
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (!_manuallyConfigured)
			{
				if (this.fieldInfo != null)
				{
					var attrib = this.fieldInfo.GetCustomAttributes(typeof(BaseCharacterModifier.ConfigAttribute), true).FirstOrDefault() as BaseCharacterModifier.ConfigAttribute;
					if (attrib != null)
					{
						_alwaysExpanded = attrib.alwaysExpanded;
					}
				}
			}
			float ph = 0;
			if (!property.isExpanded && !_alwaysExpanded)
			{
				ph = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
			}
			else
			{
				if(_alwaysExpanded)
                {
                    ph = ((EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 6);
                }
                else
                {
                    ph = ((EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 7);
                }
            }
			return ph;
		}

		private void Init(SerializedProperty property)
		{
			if (!initialized)
			{
				_target = fieldInfo.GetValue(property.serializedObject.targetObject) as BaseCharacterModifier;
				initialized = true;
			}
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			Init(property);

			if (!_manuallyConfigured)
			{
				if (this.fieldInfo != null)
				{
					var attrib = this.fieldInfo.GetCustomAttributes(typeof(BaseCharacterModifier.ConfigAttribute), true).FirstOrDefault() as BaseCharacterModifier.ConfigAttribute;
					if (attrib != null)
					{
						_alwaysExpanded = attrib.alwaysExpanded;
					}
				}
			}
			if (!_alwaysExpanded)
			{
				var foldoutRect = new Rect(position.xMin, position.yMin, position.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));
				property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label);
			}
			if (property.isExpanded || _alwaysExpanded)
			{
				EditorGUI.indentLevel++;
				position = EditorGUI.IndentedRect(position);
				if (!_alwaysExpanded)
                {
                    position.yMin = position.yMin + (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
                }

                // Don't make child fields be indented
                var indent = EditorGUI.indentLevel;
				EditorGUI.indentLevel = 0;

				var adjustScaleProp = property.FindPropertyRelative("_adjustScale");
				var adjustHeightProp = property.FindPropertyRelative("_adjustHeight");
				var adjustRadiusProp = property.FindPropertyRelative("_adjustRadius");
				var adjustMassProp = property.FindPropertyRelative("_adjustMass");
				var updateBoundsProp = property.FindPropertyRelative("_updateBounds");
				var tightenBoundsProp = property.FindPropertyRelative("_tightenBounds");
				var adjustBoundsProp = property.FindPropertyRelative("_adjustBounds");

				var scaleAdjustProp = property.FindPropertyRelative("_scale");
				var scaleBoneProp = property.FindPropertyRelative("_bone");
				var scaleBoneHashProp = property.FindPropertyRelative("_scaleBoneHash");

				var headRatioProp = property.FindPropertyRelative("_headRatio");
				var radiusAdjustYProp = property.FindPropertyRelative("_radiusAdjustY");
				var radiusAdjustProp = property.FindPropertyRelative("_radiusAdjust");
				var massAdjustProp = property.FindPropertyRelative("_massAdjust");
				var boundAdjustProp = property.FindPropertyRelative("_boundsAdjust");

				//overall rects
				var scaleRect = new Rect(position.xMin, position.yMin, position.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));
				var heightRect = new Rect(position.xMin, scaleRect.yMax, position.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));
				var radiusRect = new Rect(position.xMin, heightRect.yMax, position.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));
				var massRect = new Rect(position.xMin, radiusRect.yMax, position.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));
				var boundsBoolsRect = new Rect(position.xMin, massRect.yMax, position.width, (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));

				//labelsRects are all 120f wide or maybe  1/3rd or whatever unity makes them by standard
				var scaleLabelRect = new Rect(position.xMin, scaleRect.yMin, (position.width / 3), scaleRect.height);
				var heightLabelRect = new Rect(position.xMin, heightRect.yMin, (position.width / 3), scaleRect.height);
				var radiusLabelRect = new Rect(position.xMin, radiusRect.yMin, (position.width / 3), scaleRect.height);
				var massLabelRect = new Rect(position.xMin, massRect.yMin, (position.width / 3), scaleRect.height);
				var boundsLabelRect = new Rect(position.xMin, boundsBoolsRect.yMin, (position.width / 3), scaleRect.height);
				var boundsLabel1Rect = new Rect(boundsLabelRect.xMax, boundsBoolsRect.yMin, (position.width / 3), scaleRect.height);
				var boundsLabel2Rect = new Rect(position.xMin, boundsBoolsRect.yMax, (position.width / 3), scaleRect.height);

				//fieldsRects are whatever is left
				var scaleFieldsRect = new Rect(scaleLabelRect.xMax, scaleRect.yMin, (position.width / 3) * 2, scaleRect.height);
				var scaleFields1FieldRect = new Rect(scaleFieldsRect.xMin, scaleFieldsRect.yMin, (scaleFieldsRect.width / 3), EditorGUIUtility.singleLineHeight);
				var scaleFields2LabelRect = new Rect(scaleFields1FieldRect.xMax, scaleFieldsRect.yMin, 40f, EditorGUIUtility.singleLineHeight);
				var scaleFields2FieldRect = new Rect(scaleFields2LabelRect.xMax, scaleFieldsRect.yMin, ((scaleFieldsRect.width / 3) * 2) - 40f, EditorGUIUtility.singleLineHeight);

				var heightFieldsRect = new Rect(scaleLabelRect.xMax, heightRect.yMin, (position.width / 3) * 2, scaleRect.height);
				var heightFields1FieldRect = new Rect(heightFieldsRect.xMin, heightFieldsRect.yMin, ((heightFieldsRect.width / 3) * 2), EditorGUIUtility.singleLineHeight);
				var heightFields2FieldRect = new Rect(heightFields1FieldRect.xMax, heightFieldsRect.yMin, (heightFieldsRect.width / 3), EditorGUIUtility.singleLineHeight);

				var radiusFieldsRect = new Rect(scaleLabelRect.xMax, radiusRect.yMin, (position.width / 3) * 2, scaleRect.height);
				var radiusFields1FieldRect = new Rect(radiusFieldsRect.xMin, radiusFieldsRect.yMin, (radiusFieldsRect.width / 2), EditorGUIUtility.singleLineHeight);
				var radiusFields2FieldRect = new Rect(radiusFields1FieldRect.xMax, radiusFieldsRect.yMin, (radiusFieldsRect.width / 2), EditorGUIUtility.singleLineHeight);


				var massFieldsRect = new Rect(scaleLabelRect.xMax, massRect.yMin, (position.width / 3) * 2, scaleRect.height);
				var boundsFieldsRect = new Rect(scaleLabelRect.xMax, boundsBoolsRect.yMax, (position.width / 3) * 2, scaleRect.height);

				float prevlabelWidth = EditorGUIUtility.labelWidth;

				//you loose the tooltips when you do toggleLeft WTFF?!?
				//none of the tooltips show in play mode either!! What a fucking pain...
				var scaleLabel = EditorGUI.BeginProperty(scaleLabelRect, new GUIContent(adjustScaleProp.displayName), adjustScaleProp);
				adjustScaleProp.boolValue = EditorGUI.ToggleLeft(scaleLabelRect, scaleLabel, adjustScaleProp.boolValue);
				EditorGUI.EndProperty();

				EditorGUI.BeginDisabledGroup(!adjustScaleProp.boolValue);
				EditorGUIUtility.labelWidth = 40f;
				if(Application.isPlaying && _target.liveScale != -1)
				{
					EditorGUI.BeginDisabledGroup(true);
					EditorGUI.LabelField(scaleFields1FieldRect, new GUIContent("Scale", "The live scale is being modified by a converter above. Please exit playmode if you want to edit the base scale"));
					var fieldRect = new Rect(scaleFields1FieldRect.xMin + 40f, scaleFields1FieldRect.yMin, scaleFields1FieldRect.width -40f, scaleFields1FieldRect.height);
					EditorGUI.FloatField(fieldRect, _target.liveScale);
					EditorGUI.EndDisabledGroup();

				}
				else
				{
					scaleAdjustProp.floatValue = EditorGUI.FloatField(scaleFields1FieldRect, "Scale", scaleAdjustProp.floatValue);

				}
				EditorGUI.LabelField(scaleFields2LabelRect, " Bone");
				EditorGUI.BeginChangeCheck();
				//I want this to draw a bone selection popup when it can- i.e. when drawn inside a ConverterBehaviour and we are in play mode.
				scaleBoneProp.stringValue = EditorGUI.TextField(scaleFields2FieldRect, scaleBoneProp.stringValue);
				if (EditorGUI.EndChangeCheck())
				{
					scaleBoneHashProp.intValue = UMAUtils.StringToHash(scaleBoneProp.stringValue);
				}
				EditorGUI.EndDisabledGroup();

				var heightLabel = EditorGUI.BeginProperty(heightLabelRect, new GUIContent(adjustHeightProp.displayName), adjustHeightProp);
				adjustHeightProp.boolValue = EditorGUI.ToggleLeft(heightLabelRect, heightLabel, adjustHeightProp.boolValue);
				EditorGUI.EndProperty();

				EditorGUI.BeginDisabledGroup(!adjustHeightProp.boolValue);
				EditorGUIUtility.labelWidth = 80f;
				headRatioProp.floatValue = EditorGUI.FloatField(heightFields1FieldRect, "Head Ratio", headRatioProp.floatValue);
				EditorGUIUtility.labelWidth = 20f;
				radiusAdjustYProp.floatValue = EditorGUI.FloatField(heightFields2FieldRect, " Y ", radiusAdjustYProp.floatValue);
				EditorGUI.EndDisabledGroup();

				var radiusLabel = EditorGUI.BeginProperty(radiusLabelRect, new GUIContent(adjustRadiusProp.displayName), adjustRadiusProp);
				adjustRadiusProp.boolValue = EditorGUI.ToggleLeft(radiusLabelRect, radiusLabel, adjustRadiusProp.boolValue);
				EditorGUI.EndProperty();
				EditorGUI.BeginDisabledGroup(!adjustRadiusProp.boolValue);
				var radiusV2 = radiusAdjustProp.vector2Value;
				EditorGUI.BeginChangeCheck();
				radiusV2.x = EditorGUI.FloatField(radiusFields1FieldRect, "X ", radiusV2.x);
				radiusV2.y = EditorGUI.FloatField(radiusFields2FieldRect, " Z ", radiusV2.y);
				if (EditorGUI.EndChangeCheck())
				{
					radiusAdjustProp.vector2Value = radiusV2;
				}
				EditorGUI.EndDisabledGroup();

				var massLabel = EditorGUI.BeginProperty(massLabelRect, new GUIContent(adjustMassProp.displayName), adjustMassProp);
				adjustMassProp.boolValue = EditorGUI.ToggleLeft(massLabelRect, massLabel, adjustMassProp.boolValue);
				EditorGUI.EndProperty();
				EditorGUI.BeginDisabledGroup(!adjustMassProp.boolValue);
				EditorGUI.PropertyField(massFieldsRect, massAdjustProp, GUIContent.none);
				EditorGUI.EndDisabledGroup();

				var boundsLabel = EditorGUI.BeginProperty(boundsLabelRect, new GUIContent(updateBoundsProp.displayName), updateBoundsProp);
				updateBoundsProp.boolValue = EditorGUI.ToggleLeft(boundsLabelRect, boundsLabel, updateBoundsProp.boolValue);
				EditorGUI.EndProperty();
				var boundsLabel1 = EditorGUI.BeginProperty(boundsLabelRect, new GUIContent(tightenBoundsProp.displayName), tightenBoundsProp);
				tightenBoundsProp.boolValue = EditorGUI.ToggleLeft(boundsLabel1Rect, boundsLabel1, tightenBoundsProp.boolValue);
				EditorGUI.EndProperty();
				var boundsLabel2 = EditorGUI.BeginProperty(boundsLabel2Rect, new GUIContent(adjustBoundsProp.displayName), adjustBoundsProp);
				adjustBoundsProp.boolValue = EditorGUI.ToggleLeft(boundsLabel2Rect, boundsLabel2, adjustBoundsProp.boolValue);
				EditorGUI.EndProperty();
				EditorGUI.BeginDisabledGroup(!adjustBoundsProp.boolValue);
				EditorGUI.PropertyField(boundsFieldsRect, boundAdjustProp, GUIContent.none);
				EditorGUI.EndDisabledGroup();

				property.serializedObject.ApplyModifiedProperties();

				EditorGUIUtility.labelWidth = prevlabelWidth;
				// Set indent back to what it was
				EditorGUI.indentLevel = indent;

				EditorGUI.indentLevel--;
			}
			EditorGUI.EndProperty();
		}
	}
}
