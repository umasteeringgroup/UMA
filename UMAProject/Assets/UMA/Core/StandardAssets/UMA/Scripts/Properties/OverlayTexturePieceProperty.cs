using System;
using UnityEngine;

namespace UMA
{
	using UnityEditor;
	public class OverlayTexturePieceProperty : BasePieceProperty<OverlayTextureProperty>
	{
		#if UNITY_EDITOR
		public override float GetInspectorHeight()
		{
			return CalculateElementHeight(1);
		}
		
		public override void DrawInspectorProperties(InspectorRect rect, bool isActive, bool isFocused)
		{
			value.textureIndex = EditorGUI.IntField(rect.GetLineRect(), new GUIContent("Texture Index", "Index in the overlay data texture array"), value.textureIndex);
		}
		#endif		
	}
}