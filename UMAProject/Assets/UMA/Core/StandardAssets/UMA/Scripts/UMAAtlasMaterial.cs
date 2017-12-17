using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[CreateAssetMenu(menuName ="UMA/Atlas Material")]
	public class UMAAtlasMaterial : UMAMappedOwnedPropertyAsset
	{
		public Material materialTemplate;

		Material _material;
		public Material PrepareMaterial()
		{
			_material = new Material(materialTemplate);
			_material.name = name;
			return _material;
		}

		public override void SetDestinationPropertyValue<T>(BasePieceProperty<T> property, T value)
		{
			var textureProperty = value as TextureProperty;
			_material.SetTexture(property.name, textureProperty.value);
		}

#if UNITY_EDITOR
		public Shader materialShader { get { return materialTemplate != null ? materialTemplate.shader : null; } }

		private List<string> shaderPropertyNames = new List<string>();
		private static readonly Type texturePropertyType = typeof(TextureProperty);

		public override int GetDestinationPropertyCount()
		{
			shaderPropertyNames.Clear();
			int count = UMAPropertyUtility.GetShaderPropertyCount(materialShader);
			for (int i = 0; i < count; i++)
			{
				if (UMAPropertyUtility.GetShaderPropertyType(materialShader, i) == texturePropertyType)
				{
					shaderPropertyNames.Add(UMAPropertyUtility.GetShaderPropertyName(materialShader, i));
				}
			}
			return shaderPropertyNames.Count;
		}

		public override string GetDestinationPropertyName(int index)
		{
			return shaderPropertyNames[index];
		}

		public override Type GetDestinationPropertyType(int index)
		{
			return texturePropertyType;
		}
#endif
	}
}