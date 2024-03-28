// from http://www.sharkbombs.com/2015/02/17/unity-editor-enum-flags-as-toggle-buttons/
// Extended by Tassim 

using UnityEditor;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
	[CustomPropertyDrawer(typeof(EnumFlagsAttribute))]
	public class EnumFlagsAttributeDrawer : PropertyDrawer
	{

		public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
		{
			int namesIntModifier = 1;
			//check if there is a 'none' value at the second position too because we dont want that either- not a very elegant solution...
			if (prop.enumNames[1] == "none")
            {
                namesIntModifier = 2;
            }

            bool[] buttons = new bool[prop.enumNames.Length - namesIntModifier];
			
			if (label != GUIContent.none)
			{
				EditorGUI.LabelField(new Rect(pos.x, pos.y, EditorGUIUtility.labelWidth, pos.height), label);
			}
			EditorGUI.indentLevel++;
			// Handle button value
			EditorGUI.BeginChangeCheck();

			int buttonsValue = 0;
            for (int i = 0; i < buttons.Length; i++)
			{

				// Check if the button is/was pressed 
				if ((prop.intValue & (namesIntModifier << i)) == (namesIntModifier << i))
				{
					buttons[i] = true;
				}
				
				buttons[i] = EditorGUILayout.ToggleLeft(prop.enumNames[i + namesIntModifier].BreakupCamelCase(), buttons[i]);
				if (buttons[i])
				{
					buttonsValue += namesIntModifier << i;
				}			
			}

			// This is set to true if a control changed in the previous BeginChangeCheck block
			if (EditorGUI.EndChangeCheck())
			{
				prop.intValue = buttonsValue;
			}
			EditorGUI.indentLevel--;
		}
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return 0f;
		}
	}
}
