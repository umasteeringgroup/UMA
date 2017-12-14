using System;
using UnityEngine;

namespace UMA
{
	using UnityEditor;
	public class TexturePieceProperty : BasePieceProperty<TextureProperty>
	{
		#if UNITY_EDITOR
		public override float GetInspectorHeight()
		{
			return (propertyType == PropertyType.Constant || propertyType == PropertyType.Public) ? 90 : 0;
		}
		
		public override void DrawInspectorProperties(InspectorRect rect, bool isActive, bool isFocused)
		{
			switch (propertyType)
			{
			case PropertyType.Constant:
				value.value = EditorGUI.ObjectField(rect.GetLineRect(90), "Texture", value.value, typeof(Texture), false) as Texture;
				break;
			case PropertyType.Public:
				value.value = EditorGUI.ObjectField(rect.GetLineRect(90), "Default", value.value, typeof(Texture), false) as Texture;
				break;
			default:
				break;
			}
		}
		#endif		
	}
}