using UnityEngine;
using UnityEditor;

namespace UMA
{
	
	[CustomPropertyDrawer(typeof(ColorProperty))]
	public class ColorPropertyDrawer : PropertyDrawer 
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
			
			//EditorGUI.BeginProperty(position, label, property);
			//GUILayout.BeginArea(position);
			
			//EditorGUILayout.PropertyField(property.FindPropertyRelative("color"));
			//GUILayout.EndArea();
			
        //// Draw label
			
        //// Don't make child fields be indented
		//	var indent = EditorGUI.indentLevel;
		//	EditorGUI.indentLevel = 0;
			
        //// Calculate rects
		//	var amountRect = new Rect(position.x, position.y, 30, position.height);
		//	var unitRect = new Rect(position.x + 35, position.y, 50, position.height);
		//	var nameRect = new Rect(position.x + 90, position.y, position.width - 90, position.height);
			
        //// Draw fields - passs GUIContent.none to each so they are drawn without labels
		//	EditorGUI.PropertyField(amountRect, property.FindPropertyRelative("amount"), GUIContent.none);
		//	EditorGUI.PropertyField(unitRect, property.FindPropertyRelative("unit"), GUIContent.none);
		//	EditorGUI.PropertyField(nameRect, property.FindPropertyRelative("name"), GUIContent.none);
			
        //// Set indent back to what it was
		//	EditorGUI.indentLevel = indent;
			
			EditorGUI.EndProperty();
		}
	}	
}