using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	//Draws a 'DropList' that will accept objects that use the IDNAConverter interface (DNAConverterBehaviours - legacy prefabs- and DNAConverterControllers- the new ScriptableObjects)
	[CustomPropertyDrawer(typeof(DNAConverterList), true)]
	public class DNAConverterListPropertyDrawer : PropertyDrawer
	{
		private float dropAreaHeight = 50f;
		private float horizPadding = 2f;

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (property.isExpanded)
			{
				var h = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
				var convertersProp = property.FindPropertyRelative("_converters");
				for (int i = 0; i < convertersProp.arraySize; i++)
					h += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
				h += (EditorGUIUtility.standardVerticalSpacing);
				return h + dropAreaHeight;
			}
			else
			{
				return EditorGUI.GetPropertyHeight(property, true);
			}

		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{

			label = EditorGUI.BeginProperty(position, label, property);

			//EditorGUI.PropertyField(position, property, label, true);

			var foldoutRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label);

			//when this is expanded we want to draw a drop area where DNAConverterBahaviour prefabs and DnaConverterControllers can both be added
			if (property.isExpanded)
			{
				EditorGUI.indentLevel++;
				position = EditorGUI.IndentedRect(position);
				var indexesToRemove = new List<int>();
				var convertersProp = property.FindPropertyRelative("_converters");
				//Cant use GUILayout in a property drawer!!
				Rect dropArea = new Rect(position.xMin, foldoutRect.yMax, position.width, dropAreaHeight + EditorGUIUtility.standardVerticalSpacing);
				GUI.Box(dropArea, "Drag Converter Behaviours or Converter Controllers here");
				EditorGUI.indentLevel--;
				var entryRect = new Rect(position.xMin, dropArea.yMax, position.width -20f - horizPadding, EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
				var entryRemoveRect = new Rect(entryRect.xMax + horizPadding, dropArea.yMax, 20f, EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
				//then draw a field for each entry with an 'x' button next to it
				for (int i = 0; i < convertersProp.arraySize; i++)
				{
					//GUILayout.BeginHorizontal();
					var fieldRect = new Rect(entryRect.xMin, entryRect.yMin + EditorGUIUtility.standardVerticalSpacing, entryRect.width, entryRect.height - EditorGUIUtility.standardVerticalSpacing);

					//really we want the 'dot' selector here to show a popup window of DnaConverterControllers? 
					//TODO We should be able to use DNAConverterFields methods to draw this
					convertersProp.GetArrayElementAtIndex(i).objectReferenceValue = EditorGUI.ObjectField(fieldRect, convertersProp.GetArrayElementAtIndex(i).objectReferenceValue, typeof(IDNAConverter), false);

					var butRect = new Rect(entryRemoveRect.xMin, entryRemoveRect.yMin + EditorGUIUtility.standardVerticalSpacing, entryRemoveRect.width, entryRemoveRect.height - EditorGUIUtility.standardVerticalSpacing);
					if (GUI.Button(butRect, "x"))
					{
						indexesToRemove.Add(i);
					}

					//GUILayout.EndHorizontal();
					entryRect.y += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
					entryRemoveRect.y += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
				}
				if (indexesToRemove.Count > 0)
				{
					for(int i = 0; i < indexesToRemove.Count; i++)
					{
						//DeleteArrayElementAtIndex just seems to clear the value rather than remove the entry completely
						RemoveValueAtIndex(convertersProp, indexesToRemove[i]);
					}

				}
				DropAreaGUI(dropArea, convertersProp);
				//EditorGUI.indentLevel--;
			}
			EditorGUI.EndProperty();
		}

		private void SetValueAtIndex(SerializedProperty property, int index, Object value)
		{
			property.GetArrayElementAtIndex(index).objectReferenceValue = value;
		}

		private Object GetValueAtIndex(SerializedProperty property, int index)
		{
			return property.GetArrayElementAtIndex(index).objectReferenceValue;
		}

		private void AddValue(SerializedProperty property, Object value)
		{
			property.arraySize++;
			SetValueAtIndex(property, property.arraySize - 1, value);
		}

		private void RemoveValueAtIndex(SerializedProperty property, int index)
		{

			for (int i = index; i < property.arraySize - 1; i++)
			{

				SetValueAtIndex(property, i, GetValueAtIndex(property, i + 1));
			}

			property.arraySize--;
		}

		//We could probably do with a Utility here to draw this kind of thing universally since DefaultWardrobeRecipes and WardrobeCollections Arbitrary recipes, 
		//and the slot/overlay libraries also have the same thing
		//draws a list like this maybe something like a 'DropList' that handles the drawing of the area, and the items in the list
		//TODO make this 'click to pick' DynamicDNAConverterControllers
		private void DropAreaGUI(Rect dropArea, SerializedProperty converterListProp)
		{
			var evt = Event.current;

			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
					//can we make this show 'rejected' if the object wont get added for any reason?
				}
			}
			if (evt.type == EventType.DragPerform)
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.AcceptDrag();
					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
					IDNAConverter IDCObj = null;
					if(draggedObjects[0] is IDNAConverter)
					{
						IDCObj = draggedObjects[0] as IDNAConverter;
					}
					else if(draggedObjects[0].GetType() == typeof(GameObject))
					{
						if((draggedObjects[0] as GameObject).GetComponent<IDNAConverter>() != null)
						{
							IDCObj = (draggedObjects[0] as GameObject).GetComponent<IDNAConverter>();
						}
					}
					if (IDCObj != null)
					{
						bool canAdd = true;
						for (int i = 0; i < converterListProp.arraySize; i++)
						{
							if (converterListProp.GetArrayElementAtIndex(i).objectReferenceValue == IDCObj as UnityEngine.Object)
								canAdd = false;
						}
						if (canAdd)
						{
							converterListProp.arraySize++;
							converterListProp.GetArrayElementAtIndex(converterListProp.arraySize - 1).objectReferenceValue = IDCObj as UnityEngine.Object;
							converterListProp.serializedObject.ApplyModifiedProperties();
							GUI.changed = true;
						}
						else
						{
							DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
						}
					}
					else
					{
						DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
					}
				}
			}
		}

	}
}
