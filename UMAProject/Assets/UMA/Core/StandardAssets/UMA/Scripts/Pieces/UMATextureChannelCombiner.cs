using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	[CreateAssetMenu(menuName ="UMA/Texture Channel Combiner")]
	public class UMATextureChannelCombiner : UMAMappedOwnedPropertyAsset
	{
		public Shader combineShader;

		Material _material;
		public Material PrepareMaterial()
		{
			_material = new Material(combineShader);
			_material.name = name;
			return _material;
		}

		public override void SetDestinationPropertyValue<T>(BasePieceProperty<T> property, T value)
		{
			var valueType = typeof(T);
			if (valueType == typeof(ColorProperty))
			{
				_material.SetColor(property.name, (value as ColorProperty).color);
			}
			else if (valueType == typeof(FloatProperty))
			{
				_material.SetFloat(property.name, (value as FloatProperty).value);
			}
			else if (valueType == typeof(TextureProperty))
			{
				_material.SetTexture(property.name, (value as TextureProperty).value);
			}
			throw new NotImplementedException();
		}

#if UNITY_EDITOR

		public override int GetDestinationPropertyCount()
		{
			return combineShader == null ? 0 : ShaderUtil.GetPropertyCount(combineShader);
		}

		public override string GetDestinationPropertyName(int index)
		{
			return ShaderUtil.GetPropertyName(combineShader, index);
		}

		public override Type GetDestinationPropertyType(int index)
		{
			return ConvertShaderPropertyTypeToPiecePropertyType(ShaderUtil.GetPropertyType(combineShader, index));
		}

		private Type ConvertShaderPropertyTypeToPiecePropertyType(ShaderUtil.ShaderPropertyType shaderPropertyType)
		{
			switch (shaderPropertyType)
			{
				case ShaderUtil.ShaderPropertyType.Color:
					return typeof(ColorProperty);
				case ShaderUtil.ShaderPropertyType.Vector:
					return typeof(FloatProperty);
				case ShaderUtil.ShaderPropertyType.Float:
					return typeof(FloatProperty);
				case ShaderUtil.ShaderPropertyType.Range:
					return typeof(FloatProperty);
				case ShaderUtil.ShaderPropertyType.TexEnv:
					return typeof(TextureProperty);
				default:
					break;
			}
			throw new NotImplementedException();
		}
#endif
	}
}