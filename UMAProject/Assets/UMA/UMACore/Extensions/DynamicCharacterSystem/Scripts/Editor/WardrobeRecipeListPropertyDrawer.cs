#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UMA;
using UMACharacterSystem;

[CustomPropertyDrawer (typeof(DynamicCharacterAvatar.WardrobeRecipeList))]
public class WardrobeRecipeListPropertyDrawer : PropertyDrawer {

	float padding = 2f;
	public DynamicCharacterSystem thisDCS;
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
						if (tempRecipeAsset && (tempRecipeAsset.recipeType == "Wardrobe" || tempRecipeAsset.recipeType == "WardrobeCollection"))
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
	//TODO this needs to know its DCA
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
			var warningStyle = new GUIStyle(EditorStyles.miniButton);
			warningStyle.contentOffset = new Vector2(0f, 0f);
			warningStyle.fontStyle = FontStyle.Bold;
			var currentTint = GUI.color;
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
				if (DynamicAssetLoader.Instance)
				{
					if (!CheckRecipeAvailability(thisElement.FindPropertyRelative("_recipeName").stringValue))
					{
						var warningRect = new Rect((valRBut.xMin - 25f),valRBut.yMin,20f,valRBut.height);
						GUI.color = new Color(255, 200, 0);
                        GUI.Box(warningRect, new GUIContent("!", thisElement.FindPropertyRelative("_recipeName").stringValue + " was not in a Resources folder or an asset bundle. You need to add it to one of these to make it 'LIVE'"), warningStyle);
						GUI.color = currentTint;
					}
				}
				if (GUI.Button (valRBut, "X")) {
					thisRecipesProp.DeleteArrayElementAtIndex(i);
					thisRecipesProp.serializedObject.ApplyModifiedProperties ();
				}
			}
			DropAreaGUI (dropArea, thisRecipesProp);
		}
		EditorGUI.EndProperty ();
	}
	/// <summary>
	/// with wardobeRecipes, DynamicCharacterSystem does not have a list of refrenced recipes like the other libraries
	/// so the only way to get them is from DynamicAssetLoader (which is how DCS gets them) 
	/// so they MUST be in an assetBundle or in Resources or there is no way of finding them
	/// </summary>
	/// <param name="recipeName"></param>
	/// <returns></returns>
	private bool CheckRecipeAvailability(string recipeName)
	{
		if (Application.isPlaying)
			return true;
		bool searchResources = true;
		bool searchAssetBundles = true;
		string resourcesFolderPath = "";
		string assetBundlesToSearch = "";
		if(thisDCS != null)
		{
			searchResources = thisDCS.dynamicallyAddFromResources;
			searchAssetBundles = thisDCS.dynamicallyAddFromAssetBundles;
			resourcesFolderPath = thisDCS.resourcesRecipesFolder;
			assetBundlesToSearch = thisDCS.assetBundlesForRecipesToSearch;
		}
		bool found = false;
		DynamicAssetLoader.Instance.debugOnFail = false;
		found = DynamicAssetLoader.Instance.AddAssets<UMAWardrobeRecipe>(searchResources, searchAssetBundles, true, assetBundlesToSearch, resourcesFolderPath, null, recipeName, null);
		if (!found)
			found = DynamicAssetLoader.Instance.AddAssets<UMATextRecipe>(searchResources, searchAssetBundles, true, assetBundlesToSearch, resourcesFolderPath, null, recipeName, null);
		if (!found)
			found = DynamicAssetLoader.Instance.AddAssets<UMAWardrobeCollection>(searchResources, searchAssetBundles, true, assetBundlesToSearch, resourcesFolderPath, null, recipeName, null);
		DynamicAssetLoader.Instance.debugOnFail = true;
		return found;
	}
}
#endif
