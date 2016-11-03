#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UMACharacterSystem;

[CustomPropertyDrawer (typeof(DynamicCharacterAvatar.WardrobeRecipeList))]
public class WardrobeRecipeListPropertyDrawer : PropertyDrawer {

	float padding = 2f;
	//Make a drop area for wardrobe recipes
	private void DropAreaGUI(Rect dropArea, SerializedProperty thisRecipesProp)
	{
		var evt = Event.current;

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
				for (int i = 0; i < draggedObjects.Length; i++)
				{
					if (draggedObjects[i])
					{
						UMATextRecipe tempRecipeAsset = draggedObjects[i] as UMATextRecipe;
						if (tempRecipeAsset && tempRecipeAsset.recipeType == "Wardrobe")
						{
							bool needToAddNew = true;
							for (int ii = 0; ii < thisRecipesProp.arraySize; ii++) {
								SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(ii);
								if(thisElement.FindPropertyRelative("_recipeName").stringValue == tempRecipeAsset.name){
                                    thisRecipesProp.GetArrayElementAtIndex(ii).FindPropertyRelative("_recipe").objectReferenceValue = tempRecipeAsset;
                                    int compatibleRacesArraySize = tempRecipeAsset.compatibleRaces.Count;
                                    thisRecipesProp.GetArrayElementAtIndex(ii).FindPropertyRelative("_compatibleRaces").arraySize = compatibleRacesArraySize;
                                    for (int cr = 0; cr < compatibleRacesArraySize; cr++)
                                    {
                                        thisRecipesProp.GetArrayElementAtIndex(ii).FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue = tempRecipeAsset.compatibleRaces[cr];
                                    }
                                    needToAddNew = false;
								}
							}
							if(needToAddNew){
								int newArrayElIndex = thisRecipesProp.arraySize;
								thisRecipesProp.InsertArrayElementAtIndex(newArrayElIndex);
								thisRecipesProp.serializedObject.ApplyModifiedProperties();
								thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_recipeName").stringValue = tempRecipeAsset.name;
								thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_recipe").objectReferenceValue = tempRecipeAsset;
                                int compatibleRacesArraySize = tempRecipeAsset.compatibleRaces.Count;
                                thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_compatibleRaces").arraySize = compatibleRacesArraySize;
                                for (int cr = 0; cr < compatibleRacesArraySize; cr++)
                                {
                                    thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue = tempRecipeAsset.compatibleRaces[cr];
                                }
                                thisRecipesProp.serializedObject.ApplyModifiedProperties();
								GUI.changed = true;
							}
							continue;
						}
					}
				}
			}
		}
	}
	public override float GetPropertyHeight(SerializedProperty property, GUIContent label){
		float h = EditorGUIUtility.singleLineHeight + padding;
		SerializedProperty foldoutProp1 = property.FindPropertyRelative ("loadDefaultRecipes");
		int extraLines = 0;
		if (foldoutProp1.isExpanded) {
			var thisRecipesProp = property.FindPropertyRelative ("recipes");
			extraLines = thisRecipesProp.arraySize;
			h *= (extraLines+2);
			h += 50f + padding;
		}
		return h;
	}
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label){
		EditorGUI.BeginProperty (position, label, property);
		var r0 = new Rect (position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
		SerializedProperty foldoutProp1 = property.FindPropertyRelative ("loadDefaultRecipes");
		foldoutProp1.isExpanded = EditorGUI.Foldout (r0, foldoutProp1.isExpanded, "Default Wardrobe Recipes");
		if (foldoutProp1.isExpanded) {
			var valR = r0;
			valR = new Rect (valR.xMin, valR.yMax, valR.width, EditorGUIUtility.singleLineHeight);
			EditorGUI.PropertyField (valR,property.FindPropertyRelative ("loadDefaultRecipes"));
			Rect dropArea = new Rect (valR.xMin, (valR.yMax + padding), valR.width, 50f);
			GUI.Box (dropArea, "Drag Wardrobe Recipes here");
			valR = new Rect (valR.xMin, (valR.yMin + 50f +padding), valR.width, EditorGUIUtility.singleLineHeight);
			var thisRecipesProp = property.FindPropertyRelative ("recipes");
			float textFieldWidth = (valR.width - 20f);
			for (int i = 0; i < thisRecipesProp.arraySize; i++) {
				var valRBut = new Rect((textFieldWidth + 18f),(valR.yMax + padding), 20f, EditorGUIUtility.singleLineHeight);
				valR = new Rect (valR.xMin, (valR.yMax + padding), textFieldWidth, EditorGUIUtility.singleLineHeight);
				SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
				EditorGUI.BeginDisabledGroup(true);
                int compatibleRacesArraySize = thisElement.FindPropertyRelative("_compatibleRaces").arraySize;
                string compatibleRaces = "";
                for(int cr = 0; cr < compatibleRacesArraySize; cr++)
                {
                    compatibleRaces = compatibleRaces + thisElement.FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue;
                    if(cr < compatibleRacesArraySize -1)
                    {
                        compatibleRaces = compatibleRaces + ", ";
                    }
                }
                EditorGUI.TextField(valR,thisElement.FindPropertyRelative("_recipeName").stringValue + " ("+compatibleRaces+")");
				EditorGUI.EndDisabledGroup ();
				if (GUI.Button (valRBut, "X")) {
					thisRecipesProp.DeleteArrayElementAtIndex(i);
					thisRecipesProp.serializedObject.ApplyModifiedProperties ();
				}
			}
			DropAreaGUI (dropArea, thisRecipesProp);
		}
		EditorGUI.EndProperty ();
	}
}
#endif