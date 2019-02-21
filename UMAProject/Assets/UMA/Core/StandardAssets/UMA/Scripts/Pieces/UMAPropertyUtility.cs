using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEditor;


namespace UMA
{
	public static class UMAPropertyUtility 
	{
#if UNITY_EDITOR
		public static string[] GetShaderPropertyNames(Shader shader, string undefined = "None")
		{
			var count = GetShaderPropertyCount(shader);
			var result = new string[count+1];
			result[0] = undefined;
			for (int i = 0; i < count; i++) 
			{
				result[i+1] = GetShaderPropertyName(shader, i);
			}
			return result;
		}

		public static int GetShaderPropertyCount(Shader shader)
		{
			return shader == null ? 0 : ShaderUtil.GetPropertyCount(shader);
		}

		public static string GetShaderPropertyName(Shader shader, int index)
		{
			return ShaderUtil.GetPropertyName(shader, index);
		}

		public static Type GetShaderPropertyType(Shader shader, int index)
		{
			return ConvertShaderPropertyTypeToPiecePropertyType(ShaderUtil.GetPropertyType(shader, index));
		}

		private static Type ConvertShaderPropertyTypeToPiecePropertyType(ShaderUtil.ShaderPropertyType shaderPropertyType)
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