using UnityEngine;
using UnityEditor;

namespace UMA
{
	public class BasePropertyDrawer<T> : PropertyDrawer 
		where T : BaseProperty, new()
	//public class BasePropertyDrawer<T> : Editor 
	//	where T : BaseProperty, new()
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			
		//}
		//public override void OnInspectorGUI()
			//{
			EditorGUI.BeginChangeCheck();
			var pieceProperty = property.objectReferenceValue as BasePieceProperty<T>;

			switch (pieceProperty.propertyType)
			{
				case BasePieceProperty.PropertyType.Public:
					OnPublicGUI(pieceProperty.value);
					break;
				case BasePieceProperty.PropertyType.Constant:
					OnConstantGUI(pieceProperty.value);
					break;
				case BasePieceProperty.PropertyType.Required:
					OnRequiredGUI(pieceProperty.value);
					break;
				default:
					break;
			}

			if (EditorGUI.EndChangeCheck())
			{
				EditorUtility.SetDirty(pieceProperty);
			}
		}

		protected virtual void OnPublicGUI(T value)
		{

		}

		protected virtual void OnConstantGUI(T value)
		{
			
		}

		protected virtual void OnRequiredGUI(T value)
		{

		}
	}
}