using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	[Serializable]
	public abstract class BaseProperty
	{
		public virtual bool CanSetValueFrom(BaseProperty source)
		{
			var sourceType = source.GetType();
			var destType = GetType();
			return destType.IsAssignableFrom(sourceType) || sourceType.IsAssignableFrom(destType);
		}

		public abstract void SetValue(BaseProperty source);

#if UNITY_EDITOR
		public Type GetPiecePropertyType()
		{
			return GetPiecePropertyTypeFromPropertyType(GetType());
		}
		
		static readonly Type basePiecePropertyType = typeof(BasePieceProperty<>);
		static readonly Type basePropertyType = typeof(BaseProperty);
		
		public static Type GetPiecePropertyTypeFromPropertyType(Type propertyType)
		{
			if (_piecePropertyTypes == null)
			{
				var dict = new Dictionary<Type, Type>();
				foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					foreach(var type in assembly.GetTypes())
					{
						if (type.IsAbstract) continue;
						if (DerivesFromGeneric(type, basePiecePropertyType))
						{
							var valueType = FindGenericParentValueType(type, basePiecePropertyType);
							dict.Add(valueType, type);
						}
					}
				}
				_piecePropertyTypes = dict;
			}
			return _piecePropertyTypes[propertyType];
		}
		
		public static Type[] PropertyTypes
		{
			get 
			{
				if (_propertyTypes == null)
				{
					var results = new List<Type>();
					foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						foreach(var type in assembly.GetTypes())
						{
							if (type.IsAbstract) continue;
							if (DerivesFrom(type, basePropertyType))
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
		
		static bool DerivesFromGeneric(Type type, Type baseType)
		{
			while (type != null)
			{
				
				if (type.IsGenericType && type.GetGenericTypeDefinition() == baseType)
					return true;
				type = type.BaseType;
			}
			return false;
		}
		
		public static Type FindGenericParentValueType(Type type, Type baseType)
		{
			while (type != null)
			{
				
				if (type.IsGenericType && type.GetGenericTypeDefinition() == baseType)
				{
					return type.GetGenericArguments()[0];
				}
				type = type.BaseType;
			}
			return null;
		}
		
		
		static Type[] _propertyTypes;
		static Dictionary<Type, Type> _piecePropertyTypes;

#endif
	}
}