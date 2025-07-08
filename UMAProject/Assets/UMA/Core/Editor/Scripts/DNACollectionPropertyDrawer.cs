using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

// Assuming DNACollection is a serializable class with a List<DNA> DNAs property
// and DNA is a serializable class with some fields (e.g., name, value).
// Adjust field/property names as needed for your actual DNACollection definition.
namespace UMA
{
    [CustomPropertyDrawer(typeof(DNACollection))]
    public class DNACollectionPropertyDrawer : PropertyDrawer
    {
        public const float dropAreaHeight = 50f;
        private float horizPadding = 2f;

        private float LineHeight = EditorGUIUtility.singleLineHeight;
        private float VerticalSpacing = EditorGUIUtility.standardVerticalSpacing;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Draw foldout
            property.isExpanded = EditorGUI.Foldout(
                new Rect(position.x, position.y, position.width, LineHeight),
                property.isExpanded, label, true);


            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                var foldoutRect = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

                position = EditorGUI.IndentedRect(position);
                //Cant use GUILayout in a property drawer!!
                Rect dropArea = new Rect(position.xMin, foldoutRect.yMax, position.width, dropAreaHeight + EditorGUIUtility.standardVerticalSpacing);
                GUI.Box(dropArea, "Drag DNA Groups Here");

                UnityEngine.Object inspectMe = null;
                SerializedProperty dnaGroupsProp = property.FindPropertyRelative("DNAGroups");
                if (dnaGroupsProp != null && dnaGroupsProp.isArray)
                {
                    float y = dropAreaHeight + position.y + LineHeight + VerticalSpacing;
                    for (int i = 0; i < dnaGroupsProp.arraySize; i++)
                    {
                        SerializedProperty dnaProp = dnaGroupsProp.GetArrayElementAtIndex(i);
                        float itemHeight = EditorGUI.GetPropertyHeight(dnaProp, true);
                        string dnaName = dnaProp.objectReferenceValue != null ? dnaProp.objectReferenceValue.name : "Empty";
                        EditorGUI.PropertyField(
                            new Rect(position.x, y, position.width-60, LineHeight),
                            dnaProp, new GUIContent(dnaName), true);
                        if (GUI.Button(new Rect(position.x + position.width - 60, y, 30, LineHeight), "I"))
                        {
                            inspectMe = dnaProp.objectReferenceValue;
                        }
                        if (GUI.Button(new Rect(position.x + position.width - 30, y, 30, LineHeight), "X"))
                        {
                            dnaGroupsProp.DeleteArrayElementAtIndex(i);
                            i--; // Adjust index after deletion
                        }
                        y += itemHeight + VerticalSpacing;
                    }

                    // Add/Remove buttons
                    Rect buttonRect = new Rect(position.x, y, position.width, LineHeight);
                    if (GUI.Button(new Rect(buttonRect.x, buttonRect.y, buttonRect.width / 2 - 2, LineHeight), "Add DNA Group"))
                    {
                        dnaGroupsProp.InsertArrayElementAtIndex(dnaGroupsProp.arraySize);
                    }
                    if (dnaGroupsProp.arraySize > 0)
                    {
                        if (GUI.Button(new Rect(buttonRect.x + buttonRect.width / 2 + 2, buttonRect.y, buttonRect.width / 2 - 2, LineHeight), "Remove Last"))
                        {
                            dnaGroupsProp.DeleteArrayElementAtIndex(dnaGroupsProp.arraySize - 1);
                        }
                    }
                }
                else
                {
                    EditorGUI.LabelField(new Rect(position.x, position.y + LineHeight, position.width, LineHeight), "DNAGroups property not found or not an array.");
                }
                DropAreaGUI(dropArea, dnaGroupsProp);
                EditorGUI.indentLevel--;
                if (inspectMe != null)
                {
                    InspectorUtlity.InspectTarget(inspectMe);
                    inspectMe = null; // Reset after inspection
                }
            }

            EditorGUI.EndProperty();
        }

        private void DropAreaGUI(Rect dropArea, SerializedProperty converterListProp)
        {
            var evt = Event.current;

            if (evt.type == EventType.DragUpdated)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    //can we make this show 'rejected' if the object wont get added for any reason?
                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                    DNAGroup draggedGroup = draggedObjects[0] as DNAGroup;
                    if (draggedGroup == null)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    }
                }
            }
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                    DNAGroup draggedGroup = null;
                    draggedGroup = draggedObjects[0] as DNAGroup;
                    if (draggedGroup != null)
                    {
                        bool canAdd = true;
                        for (int i = 0; i < converterListProp.arraySize; i++)
                        {
                            if (converterListProp.GetArrayElementAtIndex(i).objectReferenceValue == draggedGroup as UnityEngine.Object)
                            {
                                canAdd = false;
                                EditorUtility.DisplayDialog("Duplicate DNA Group", "The DNA Group '" + draggedGroup.name + "' is already in the list.", "OK");
                            }
                        }
                        if (canAdd)
                        {
                            converterListProp.arraySize++;
                            converterListProp.GetArrayElementAtIndex(converterListProp.arraySize - 1).objectReferenceValue = draggedGroup as UnityEngine.Object;
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

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = LineHeight;
            if (property.isExpanded)
            {
                height += dropAreaHeight;
                SerializedProperty dnasProp = property.FindPropertyRelative("DNAGroups");
                if (dnasProp != null && dnasProp.isArray)
                {
                    for (int i = 0; i < dnasProp.arraySize; i++)
                    {
                        SerializedProperty dnaProp = dnasProp.GetArrayElementAtIndex(i);
                        height += EditorGUI.GetPropertyHeight(dnaProp, true) + VerticalSpacing;
                    }
                    height += LineHeight + VerticalSpacing; // For buttons
                }
                else
                {
                    height += LineHeight;
                }
            }
            return height;
        }
    }
}
