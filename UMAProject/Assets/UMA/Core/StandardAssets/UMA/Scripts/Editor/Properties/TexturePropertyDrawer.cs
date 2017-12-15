using UnityEngine;
using UnityEditor;

namespace UMA
{
	//[CustomEditor(typeof(TexturePieceProperty))]
	[CustomPropertyDrawer(typeof(TexturePieceProperty))]
	public class TexturePropertyDrawer : BasePropertyDrawer<TextureProperty>
	{
		protected override void OnPublicGUI(TextureProperty value)
		{
			value.value = EditorGUILayout.ObjectField("Default", value.value, typeof(Texture), false) as Texture;
		}
		
		protected override void OnConstantGUI(TextureProperty value)
		{
			value.value = EditorGUILayout.ObjectField("Texture", value.value, typeof(Texture), false) as Texture;
		}
	}	
}