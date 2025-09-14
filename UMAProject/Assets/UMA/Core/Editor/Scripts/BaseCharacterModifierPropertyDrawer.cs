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
			// We still must return a height for the IMGUI system even though we are using GUILayout inside OnGUI.
			// Provide an approximation based on expansion state so the inspector reserves enough space.
			if (!_manuallyConfigured)
			{
				if (this.fieldInfo != null)
				{
					var attrib = this.fieldInfo.GetCustomAttributes(typeof(BaseCharacterModifier.ConfigAttribute), true).FirstOrDefault() as BaseCharacterModifier.ConfigAttribute;
					if (attrib != null) { _alwaysExpanded = attrib.alwaysExpanded; }
				}
			}
			int lines = 1; // foldout line
			if (_alwaysExpanded || property.isExpanded)
			{
				// scale, height, radius, mass, bounds toggles, bounds adjust (optional)
				lines += 14; // base groups
			}
			return (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * lines;
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
            float scale = EditorGUIUtility.pixelsPerPoint;
			position.width *= scale;

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

			// Reserve the rect Unity gives us but use GUILayout inside a clipped area.
			// Begin a new area so GUILayout works in a PropertyDrawer context.
			GUI.BeginGroup(position);
			var localRect = new Rect(0,0, position.width, position.height);
			GUILayout.BeginArea(localRect);

			EditorGUI.indentLevel++;
			if (!_alwaysExpanded)
			{
				property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, label, true);
			}
			else
			{
				EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
				property.isExpanded = true; // force true for logic below
			}

			if (property.isExpanded)
			{
				DrawExpanded(property);
			}

			EditorGUI.indentLevel--;
			GUILayout.EndArea();
			GUI.EndGroup();

			EditorGUI.EndProperty();
		}

		private void DrawExpanded(SerializedProperty property)
		{
			// Fetch properties
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
			var boundsAdjustProp = property.FindPropertyRelative("_boundsAdjust");
			var manuallySetBoundsProp = property.FindPropertyRelative("_manuallySetBounds");
			var manualSetBoundsProp = property.FindPropertyRelative("_manualSetBounds");

            EditorGUILayout.Space(2f);

			// Scale Row
			adjustScaleProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Adjust Scale", adjustScaleProp.tooltip), adjustScaleProp.boolValue, GUILayout.Width(110));
			EditorGUI.BeginDisabledGroup(!adjustScaleProp.boolValue);
            EditorGUI.indentLevel += 2;
            if (Application.isPlaying && _target != null && _target.liveScale != -1)
			{
				EditorGUILayout.LabelField(new GUIContent("Scale (Live)", "The live scale is being modified by a converter above. Exit playmode to edit base scale."), GUILayout.Width(90));
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.FloatField(_target.liveScale, GUILayout.MinWidth(40));
				EditorGUI.EndDisabledGroup();
			}
			else
			{
				scaleAdjustProp.floatValue = EditorGUILayout.FloatField(new GUIContent("Scale", "Base scale multiplier"), scaleAdjustProp.floatValue, GUILayout.MinWidth(40));
			}
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(scaleBoneProp,new GUIContent("Scale Bone","The bone that is scaled by the scale property"), GUILayout.MinWidth(40));
			if (EditorGUI.EndChangeCheck())
			{
				scaleBoneHashProp.intValue = UMAUtils.StringToHash(scaleBoneProp.stringValue);
			}
			EditorGUI.indentLevel -= 2;
            EditorGUI.EndDisabledGroup();


			// Height Row
			adjustHeightProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Adjust Height", adjustHeightProp.tooltip), adjustHeightProp.boolValue, GUILayout.Width(110));
			EditorGUI.BeginDisabledGroup(!adjustHeightProp.boolValue);	
			EditorGUI.indentLevel+=2;
            EditorGUILayout.PropertyField(headRatioProp, new GUIContent("Height by Heads", "Calculate height from head bone and head ratio"), GUILayout.MinWidth(60));
			EditorGUILayout.PropertyField(radiusAdjustYProp, new GUIContent("Extra Y", "Extra Y padding for height calc"), GUILayout.MinWidth(60));
            //headRatioProp.floatValue = EditorGUILayout.FloatField(new GUIContent("Head Ratio", "How many heads tall the character is"), headRatioProp.floatValue, GUILayout.MinWidth(60));
            // radiusAdjustYProp.floatValue = EditorGUILayout.FloatField(new GUIContent("Y", "Extra Y padding for height calc"), radiusAdjustYProp.floatValue, GUILayout.Width(60));
			EditorGUI.indentLevel-=2;
            EditorGUI.EndDisabledGroup();

			// Radius Row
			EditorGUILayout.BeginHorizontal();
			adjustRadiusProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Adjust Radius", adjustRadiusProp.tooltip), adjustRadiusProp.boolValue, GUILayout.Width(110));
			EditorGUI.BeginDisabledGroup(!adjustRadiusProp.boolValue);
			EditorGUILayout.PropertyField(radiusAdjustProp, GUIContent.none, GUILayout.MinWidth(80));
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.EndHorizontal();

			// Mass Row
			EditorGUILayout.BeginHorizontal();
			adjustMassProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Adjust Mass", adjustMassProp.tooltip), adjustMassProp.boolValue, GUILayout.Width(110));
			EditorGUI.BeginDisabledGroup(!adjustMassProp.boolValue);
			EditorGUILayout.PropertyField(massAdjustProp, GUIContent.none);
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            manuallySetBoundsProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Set Bounds", manuallySetBoundsProp.tooltip), manuallySetBoundsProp.boolValue, GUILayout.Width(110));
            EditorGUI.BeginDisabledGroup(!manuallySetBoundsProp.boolValue);
            EditorGUILayout.PropertyField(manualSetBoundsProp, GUIContent.none);
			EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10f);
			EditorGUI.BeginDisabledGroup(manuallySetBoundsProp.boolValue);
            EditorGUILayout.LabelField("Legacy bounds options (not recommended)",EditorStyles.boldLabel);
            // Bounds Toggles
            //EditorGUILayout.BeginHorizontal();
            updateBoundsProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Update Bounds", updateBoundsProp.tooltip), updateBoundsProp.boolValue);
			tightenBoundsProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Tighten", tightenBoundsProp.tooltip), tightenBoundsProp.boolValue);
			//adjustBoundsProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Adjust Bounds", adjustBoundsProp.tooltip), adjustBoundsProp.boolValue);
            //EditorGUILayout.EndHorizontal();

            // Bounds Adjust Vector
            EditorGUILayout.BeginHorizontal();
            adjustBoundsProp.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Adjust Bounds", adjustBoundsProp.tooltip), adjustBoundsProp.boolValue,GUILayout.Width(110));
            EditorGUI.BeginDisabledGroup(!adjustBoundsProp.boolValue);
			EditorGUILayout.PropertyField(boundsAdjustProp, GUIContent.none);
			EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
			EditorGUI.EndDisabledGroup();

            property.serializedObject.ApplyModifiedProperties();
		}
	}
}
