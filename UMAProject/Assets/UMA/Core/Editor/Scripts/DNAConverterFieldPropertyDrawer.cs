using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	//Draws a dummy field that will take any object that uses the IDNAConverter interface (DNAConverterBehaviours - legacy prefabs- and DNAConverterControllers- the new ScriptableObjects)
	[CustomPropertyDrawer(typeof(DNAConverterField), true)]
	public class DNAConverterFieldPropertyDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			var converterProp = property.FindPropertyRelative("_converter");
			return EditorGUI.GetPropertyHeight(converterProp, true);
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);

			//we need to draw a few things to make this work. we need a background object field of type DynamicDNAConverterController so the 'pick' button shows all those
			//Then we need a TextArea that looks like a field that will show the current value and icon or 'None (DNAConverterController/Behaviour)'
			//Then over the top an invisible drop area that will accept either a DnaConverterBehaviour or a DNAConverterController
			var converterProp = property.FindPropertyRelative("_converter");

			DrawObjectReferenceField(position, converterProp, label);

			EditorGUI.EndProperty();
		}

		private void DrawObjectReferenceField(Rect position, SerializedProperty property, GUIContent label)
		{
			Vector2 iconSize = EditorGUIUtility.GetIconSize();
			EditorGUIUtility.SetIconSize(new Vector2(12f, 12f));
			DynamicDNAConverterController converterControllerObject = null;
			if(property.objectReferenceValue != null)
				converterControllerObject = property.objectReferenceValue.GetType() == typeof(DynamicDNAConverterController) ? property.objectReferenceValue as DynamicDNAConverterController : null;

			var dummyFieldStyle = new GUIStyle(EditorStyles.objectField);//could be objectFieldMiniThumb
			dummyFieldStyle.normal.background = null;

			var labelPos = new Rect(position.xMin, position.yMin, EditorGUIUtility.labelWidth, position.height);
			var fieldPos = new Rect(labelPos.xMax, position.yMin, position.width - labelPos.width, position.height);

			//unfortunately we can use PrefixLabel because it inherits the GUI.content color of the field (which is transparent)
			//the result is the label doesn't highlight- but I think we can live with that!
			EditorGUI.LabelField(labelPos, label);

			var prevContentColor = GUI.contentColor;
			GUI.contentColor = new Color(0, 0, 0, 0);//hide the content of the field so we can use our own label
			EditorGUI.BeginChangeCheck();
			//We use a converterController field rather than an object field so that the 'dot' button shows ConverterControllers when clicked
			converterControllerObject = (DynamicDNAConverterController)EditorGUI.ObjectField(fieldPos, "",converterControllerObject, typeof(DynamicDNAConverterController), false);
			if (EditorGUI.EndChangeCheck())
			{
				property.objectReferenceValue = converterControllerObject; 
			}
			GUI.contentColor = prevContentColor;

			Rect dropRect = fieldPos;
			dropRect.width = dropRect.width - 18f;

			System.Type fieldType = typeof(DynamicDNAConverterController);

			if (property.objectReferenceValue != null)
				fieldType = property.objectReferenceValue.GetType();

			GUIContent typeContent = EditorGUIUtility.ObjectContent(property.objectReferenceValue, fieldType);

			if (property.objectReferenceValue == null)
			{
				typeContent.text = "None (DNAConverterController/Behaviour)";
				typeContent.image = null;
			}

			GUI.Box(dropRect, typeContent, dummyFieldStyle);
			DoDropArea(dropRect, property);
			EditorGUIUtility.SetIconSize(iconSize);
		}
		private void DoDropArea(Rect dropArea, SerializedProperty property)
		{
			Event evt = Event.current;
			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
			}
			if (evt.type == EventType.DragPerform)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.AcceptDrag();
					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
					IDNAConverter IDCObj = null;
					if (draggedObjects[0] is IDNAConverter)
					{
						IDCObj = draggedObjects[0] as IDNAConverter;
					}
					else if (draggedObjects[0].GetType() == typeof(GameObject))
					{
						if ((draggedObjects[0] as GameObject).GetComponent<IDNAConverter>() != null)
						{
							IDCObj = (draggedObjects[0] as GameObject).GetComponent<IDNAConverter>();
						}
					}
					if (IDCObj != null)
					{
						property.objectReferenceValue = IDCObj as UnityEngine.Object;
						property.serializedObject.ApplyModifiedProperties();//Needed?
						GUI.changed = true;
					}
				}
			}
		}
	}
}
