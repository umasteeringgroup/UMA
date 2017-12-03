using UnityEngine;
using UnityEditor;
using System;

namespace UMA
{
	public static class UMAEditorGUILayout
	{
		public static Type PropertyTypeField(string label, Type type, params GUILayoutOption[] options)
		{
			var index = ArrayUtility.IndexOf(Property.PropertyTypes, type);
			EditorGUI.BeginChangeCheck();
			var newIndex = EditorGUILayout.Popup(new GUIContent(label), index, PropertyTypesGUIContents, options);
			if (EditorGUI.EndChangeCheck())
			{
				return Property.PropertyTypes[newIndex];
			}
			return type;
		}
		
		public static GUIContent[] PropertyTypesGUIContents
		{
			get
			{
				if (_propertyTypesGUIContents == null)
				{
					var propertyTypes = Property.PropertyTypes;
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
	
	}
}