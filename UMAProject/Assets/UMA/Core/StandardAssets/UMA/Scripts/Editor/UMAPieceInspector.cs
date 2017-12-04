using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomEditor(typeof(UMAPiece))]
	public class UMAPieceInspector : Editor
	{
		private bool _showProperties;

		void OnEnable()
		{
			_showProperties = EditorPrefs.GetBool("UMAPieceInspector_ShowProperties", true);
		}

		private bool ShowProperties
		{
			get
			{
				return _showProperties;
			}
			set
			{
				if (value != _showProperties)
				{
					_showProperties = value;
					EditorPrefs.SetBool("UMAPieceInspector_ShowProperties", value);
				}
			}
		}

		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();
			var piece = target as UMAPiece;
			if (piece.Properties == null)
			{
				piece.Properties = new BasePieceProperty[0];
			}

			ShowProperties = EditorGUILayout.Foldout(ShowProperties, "Properties");
			if (ShowProperties)
			{
				for (int i = piece.Properties.Length - 1; i >= 0; i--)
				{
					var property = piece.Properties[i];
					GUILayout.BeginHorizontal();
					EditorGUI.BeginChangeCheck();
					var propertyType = UMAEditorGUILayout.PropertyTypeField(property.name, property.GetPropertyType());
					if (EditorGUI.EndChangeCheck())
					{
						var newProperty = BasePieceProperty.CreateProperty(propertyType);
						newProperty.name = property.name;
						AddPropertyToAsset(piece, newProperty);
						DestroyImmediate(property, true);
						piece.Properties[i] = newProperty;
						property = newProperty;
					}
					if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
					{
						ArrayUtility.RemoveAt(ref piece.Properties, i);
						DestroyImmediate(property, true);
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
					var newProperty = BasePieceProperty.CreateProperty(BaseProperty.PropertyTypes[0]); 
					newProperty.name = "Added";
					AddPropertyToAsset(piece, newProperty);
					ArrayUtility.Insert(ref piece.Properties, 0, newProperty);
				}
			}
			
			
			if (EditorGUI.EndChangeCheck())
			{
				UnityEditor.EditorUtility.SetDirty(piece);
				UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(piece), ImportAssetOptions.ForceSynchronousImport);
			}
		}
		
		public static void DrawProperty(BasePieceProperty property)
		{
			Editor editor = Editor.CreateEditor(property); 
			editor.OnInspectorGUI();
		}
		
		public static void AddPropertyToAsset(UMAPiece piece, BasePieceProperty property)			
		{
			AssetDatabase.AddObjectToAsset(property, AssetDatabase.GetAssetPath(piece));
		}
	}
}