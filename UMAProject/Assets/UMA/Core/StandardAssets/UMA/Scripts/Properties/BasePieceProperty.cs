using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	public abstract class BasePieceProperty : ScriptableObject
	{
		public enum PropertyType
		{
			Public,
			Constant,
			Required
		}
		public PropertyType propertyType;

		public abstract BaseProperty GetValue();
		public abstract void SetValue(BaseProperty source);

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

		public virtual float GetInspectorHeight()
		{
			return 0;
		}

		public virtual void DrawInspectorProperties(InspectorRect rect, bool isActive, bool isFocused)
		{
		}
#endif
	}

	public abstract class BasePieceProperty<T> : BasePieceProperty
		where T : BaseProperty, new()
	{
		public T value = new T();

		public override void SetValue(BaseProperty source)
		{
			value.SetValue(source);
		}

		public override BaseProperty GetValue()
		{
			return value;
		}
	}
}