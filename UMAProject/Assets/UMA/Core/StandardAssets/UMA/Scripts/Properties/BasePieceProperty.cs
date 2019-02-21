using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	public class BasePieceProperty : InspectableAsset
	{
		public BasePiecePropertyData data;
		public PropertyType propertyType { get { return data.propertyType; } set { data.propertyType = value; } }

		public string propertyName { get { return base.name; } set { base.name = value; data.name = value + "_data"; } }

		public BaseProperty GetValue()
		{
			return data.GetValue();
		}
		public void SetValue(BaseProperty source)
		{
			data.SetValue(source);
		}

		public void SetName(string value)
		{
			name = value;
			data.name = value + "_data";
		}

#if UNITY_EDITOR
		public override float GetInspectorHeight()
		{
			return data.GetInspectorHeight();
		}

		public override void DrawInspectorProperties(InspectorRect rect, bool isActive, bool isFocused)
		{
			data.DrawInspectorProperties(rect, isActive, isFocused);
		}

		public void DestroyImmediate()
		{
			DestroyImmediate(data, true);
			DestroyImmediate(this, true);
		}

		public void ChangePropertyDataType(Type propertyType)
		{
			var newData = CreatePropertyData(propertyType);
			AssetDatabase.AddObjectToAsset(newData, AssetDatabase.GetAssetPath(this));
			newData.propertyType = data.propertyType;
			DestroyImmediate(data, true);
			data = newData;
		}

		public Type GetPropertyType()
		{
			return BaseProperty.FindGenericParentValueType(data.GetType(), basePiecePropertyType);
		}

		public static BasePieceProperty CreateProperty(Type propertyType, UnityEngine.Object asset)
		{
			var result = ScriptableObject.CreateInstance<BasePieceProperty>();
			result.data = CreatePropertyData(propertyType);
			AssetDatabase.AddObjectToAsset(result, asset);
			AssetDatabase.AddObjectToAsset(result.data, asset);
			return result;
		}

		protected static BasePiecePropertyData CreatePropertyData(Type propertyType)
		{
			var result = ScriptableObject.CreateInstance(BaseProperty.GetPiecePropertyTypeFromPropertyType(propertyType)) as BasePiecePropertyData;
			return result;
		}

		static readonly Type basePiecePropertyType = typeof(BasePieceProperty<>);
#endif
	}

	public enum PropertyType
	{
		Public,
		Constant,
		Required
	}

	public abstract class BasePiecePropertyData : InspectableAsset
	{
		public PropertyType propertyType;
		
		public abstract BaseProperty GetValue();
		public abstract void SetValue(BaseProperty source);
	}

	public abstract class BasePieceProperty<T> : BasePiecePropertyData
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