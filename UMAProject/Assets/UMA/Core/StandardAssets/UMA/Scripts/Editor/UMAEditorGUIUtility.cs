using UnityEngine;
using UnityEditor;
using System;

namespace UMA
{
	public static class UMAEditorGUILayout
	{
		public static Type PropertyTypeField(Rect rect, string label, Type type)
		{
			var index = ArrayUtility.IndexOf(BaseProperty.PropertyTypes, type);
			EditorGUI.BeginChangeCheck();
			var newIndex = EditorGUI.Popup(rect, new GUIContent(label), index, PropertyTypesGUIContents);
			if (EditorGUI.EndChangeCheck())
			{
				return BaseProperty.PropertyTypes[newIndex];
			}
			return type;
		}

		public static Type PropertyTypeField(string label, Type type, params GUILayoutOption[] options)
		{
			var index = ArrayUtility.IndexOf(BaseProperty.PropertyTypes, type);
			EditorGUI.BeginChangeCheck();
			var newIndex = EditorGUILayout.Popup(new GUIContent(label), index, PropertyTypesGUIContents, options);
			if (EditorGUI.EndChangeCheck())
			{
				return BaseProperty.PropertyTypes[newIndex];
			}
			return type;
		}


		public static GUIContent[] PropertyTypesGUIContents
		{
			get
			{
				if (_propertyTypesGUIContents == null)
				{
					var propertyTypes = BaseProperty.PropertyTypes;
					_propertyTypesGUIContents = new GUIContent[propertyTypes.Length];
					for (int i = 0; i < propertyTypes.Length; i++) 
					{
						_propertyTypesGUIContents[i] = new GUIContent(propertyTypes[i].Name);
					}
				}
				return _propertyTypesGUIContents;
			}
		}
		static GUIContent[] _propertyTypesGUIContents;

		public static Type ConditionTypeField(string label, Type type, params GUILayoutOption[] options)
		{
			var index = ArrayUtility.IndexOf(BaseCondition.ConditionTypes, type);
			EditorGUI.BeginChangeCheck();
			var newIndex = EditorGUILayout.Popup(new GUIContent(label), index, ConditionTypesGUIContents, options);
			if (EditorGUI.EndChangeCheck())
			{
				return BaseCondition.ConditionTypes[newIndex];
			}
			return type;
		}

		public static GUIContent[] ConditionTypesGUIContents
		{
			get
			{
				if (_conditionTypesGUIContents == null)
				{
					var conditionTypes = BaseCondition.ConditionTypes;
					_conditionTypesGUIContents = new GUIContent[conditionTypes.Length];
					for (int i = 0; i < conditionTypes.Length; i++)
					{
						_conditionTypesGUIContents[i] = new GUIContent(conditionTypes[i].Name);
					}
				}
				return _conditionTypesGUIContents;
			}
		}
		static GUIContent[] _conditionTypesGUIContents;

	}
}