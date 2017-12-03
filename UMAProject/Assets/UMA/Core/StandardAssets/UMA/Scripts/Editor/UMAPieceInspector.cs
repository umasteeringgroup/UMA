using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomEditor(typeof(UMAPiece))]
	public class UMAPieceInspector : Editor
	{
		private bool showProperties;

		public override void OnInspectorGUI()
		{
			var piece = target as UMAPiece;
			var so = new SerializedObject(piece);
			var propertiesSO = so.FindProperty("Properties");
			if (piece.Properties == null)
			{
				piece.Properties = new Property[0];
			}

			showProperties = EditorGUILayout.Foldout(showProperties, "Properties");
			if (showProperties)
			{
				for (int i = piece.Properties.Length - 1; i >= 0; i--)
				{
					var property = piece.Properties[i];
					GUILayout.BeginHorizontal();
					EditorGUI.BeginChangeCheck();
					var propertyType = UMAEditorGUILayout.PropertyTypeField(property.name, property.GetType());
					if (EditorGUI.EndChangeCheck())
					{
						var newProperty = ScriptableObject.CreateInstance(propertyType) as Property;
						newProperty.name = property.name;
						AddPropertyToAsset(piece, newProperty);
						DestroyImmediate(property, true);
						piece.Properties[i] = newProperty;
						property = newProperty;
						UnityEditor.EditorUtility.SetDirty(piece);
					}
					if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
					{
						ArrayUtility.RemoveAt(ref piece.Properties, i);
						DestroyImmediate(property, true);
						UnityEditor.EditorUtility.SetDirty(piece);
						GUILayout.EndHorizontal();
						continue;
					}
					GUILayout.EndHorizontal();

					EditorGUI.indentLevel++;
					property.name = EditorGUILayout.TextField("Name", property.name);

					DrawProperty(property);
					EditorGUI.indentLevel--;
				}
				if (GUILayout.Button("+"))
				{
					var newProperty = ScriptableObject.CreateInstance(Property.PropertyTypes[0]) as Property;
					newProperty.name = "Added";
					AddPropertyToAsset(piece, newProperty);
					ArrayUtility.Insert(ref piece.Properties, 0, newProperty);
					UnityEditor.EditorUtility.SetDirty(piece);
				}
			}
		}
		
		public static void DrawProperty(Property property)
		{
			var propertySO = new SerializedObject(property);
			var propertySP = propertySO.GetIterator();
			propertySP.NextVisible(true);
			while (propertySP.NextVisible(true))
			{
				if (propertySP.propertyType == SerializedPropertyType.Generic)
				{
					// no good way to show structs, I could show arrays... but then... meh. 
					// EditorGUILayout.LabelField("Array", propertySP.isFixedBuffer.ToString());
				}
				else
				{
					EditorGUILayout.PropertyField(propertySP, true);
				}
			}
			propertySO.ApplyModifiedProperties();
			//var startingDepth = propertySO.depth;
			//propertySO.NextVisible(true);
			//do
			//{
			//	EditorGUILayout.PropertyField(propertySO, true);
			//	propertySO.NextVisible(false);
			//} while (propertySO.depth > startingDepth);				
			//			EditorGUILayout.PropertyField(propertiesSO.GetArrayElementAtIndex(i), true);
		}
		
		public static void AddPropertyToAsset(UMAPiece piece, Property property)			
		{
			AssetDatabase.AddObjectToAsset(property, AssetDatabase.GetAssetPath(piece));
		}
	}
}