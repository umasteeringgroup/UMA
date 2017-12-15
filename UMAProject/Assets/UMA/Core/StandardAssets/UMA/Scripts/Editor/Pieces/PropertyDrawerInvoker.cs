using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA
{
	public static class PropertyDrawerInvoker
	{
		static Dictionary<string, PropertyDrawer> _cachedPropertyDrawers;
		
		public static float GetPropertyHeight(SerializedProperty property)
		{
			var pd = GetPropertyDrawer(property.type);
			if (pd != null)
			{
				Debug.Log(property.objectReferenceValue);
				return pd.GetPropertyHeight(property, new GUIContent(property.displayName));
			}
			Debug.LogWarning(property.objectReferenceValue.name);
			return EditorGUI.GetPropertyHeight(property);
		}

		public static bool PropertyField(Rect rect, SerializedProperty property)
		{
			var pd = GetPropertyDrawer(property.type);
			if (pd != null)
			{
				pd.OnGUI(rect, property, new GUIContent(property.displayName));
				return true;
			}

			return EditorGUI.PropertyField(rect, property);
		}

		private static PropertyDrawer GetPropertyDrawer(string typeName)
		{
			if (_cachedPropertyDrawers == null)
			{
				_cachedPropertyDrawers = new Dictionary<string, PropertyDrawer>();
				foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					foreach (var type in assembly.GetTypes())
					{
						var ca = type.GetCustomAttributes(typeof(UMACustomPropertyDrawerAttribute), false);
						if (ca.Length > 0)
						{
							var pdt = (ca[0] as UMACustomPropertyDrawerAttribute).type;
							var constructor = type.GetConstructor(Type.EmptyTypes);
							if (constructor != null)
							{
								var newPD = constructor.Invoke(null) as PropertyDrawer;
								_cachedPropertyDrawers[pdt.Name] = newPD;
							}							
						}
					}
				}
			}
			PropertyDrawer pd;
			_cachedPropertyDrawers.TryGetValue(typeName, out pd);
			return pd;
		}
	}
}