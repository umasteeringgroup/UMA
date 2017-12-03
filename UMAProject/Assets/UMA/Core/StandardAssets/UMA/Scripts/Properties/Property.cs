using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	public abstract class Property : ScriptableObject
	{
		#if UNITY_EDITOR
		public static Type[] PropertyTypes
		{
			get 
			{
				if (_propertyTypes == null)
				{
					var propertyType = typeof(Property);
					var results = new List<Type>();
					foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						foreach(var type in assembly.GetTypes())
						{
							if (type.IsAbstract) continue;
							if (DerivesFrom(type, propertyType))
							{
								results.Add(type);
							}
						}
					}
					_propertyTypes = results.ToArray();
				}
				return _propertyTypes;
			}
		}
		
		static bool DerivesFrom(Type type, Type baseType)
		{
			while (type != null)
			{
				if (type == baseType)
					return true;
				type = type.BaseType;
			}
			return false;
		}
		
		static Type[] _propertyTypes;
		
		#endif
	}
}