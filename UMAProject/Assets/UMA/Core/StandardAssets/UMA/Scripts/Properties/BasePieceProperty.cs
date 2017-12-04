using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	public abstract class BasePieceProperty : ScriptableObject
	{
		#if UNITY_EDITOR
		
		public static BasePieceProperty CreateProperty(Type propertyType)
		{
			return ScriptableObject.CreateInstance(BaseProperty.GetPiecePropertyTypeFromPropertyType(propertyType)) as BasePieceProperty;
		}
		
		static readonly Type basePiecePropertyType = typeof(BasePieceProperty<>);
		public Type GetPropertyType()
		{
			return BaseProperty.FindGenericParentValueType(GetType(), basePiecePropertyType);
		}
			
		#endif
	}
	
	public abstract class BasePieceProperty<T> : BasePieceProperty
		where T : BaseProperty, new()
	{
		public T value = new T();
	}
}