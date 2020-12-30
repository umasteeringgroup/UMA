#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UMA;
using System.Collections.Generic;

namespace UMA.CharacterSystem.Editors
{
    [CustomPropertyDrawer(typeof(DynamicCharacterAvatar.WardrobeRecipeList))]
    public class WardrobeRecipeListPropertyDrawer : PropertyDrawer
    {
        float padding = 2f;
        // public DynamicCharacterSystem thisDCS;
        public DynamicCharacterAvatar thisDCA;
		public bool changed = false;
		static bool defaultOpen = false;
        Texture warningIcon;
		int wardrobeRecipePickerID = -1;
        bool recipesIndexed = false;

		//Make a drop area for wardrobe recipes
		private void DropAreaGUI(Rect dropArea, SerializedProperty thisRecipesProp)
        {
            var evt = Event.current;
			//make the box clickable so that the user can select raceData assets from the asset selection window
			if (evt.type == EventType.MouseUp)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					wardrobeRecipePickerID = EditorGUIUtility.GetControlID(new GUIContent("wrObjectPicker"), FocusType.Passive);
					EditorGUIUtility.ShowObjectPicker<UMAWardrobeRecipe>(null, false, "", wardrobeRecipePickerID);
					Event.current.Use();//stops the Mismatched LayoutGroup errors
					return;
				}
			}
			if (evt.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == wardrobeRecipePickerID)
			{
				UMAWardrobeRecipe uwr = EditorGUIUtility.GetObjectPickerObject() as UMAWardrobeRecipe;
                recipesIndexed = false;
				if (AddRecipe(thisRecipesProp, uwr))
                {
                    if (recipesIndexed)
                    {
                        recipesIndexed = false;
                        UMAContextBase.Instance.ValidateDictionaries();
                    }
                }
				if (evt.type != EventType.Layout)
					Event.current.Use();//stops the Mismatched LayoutGroup errors
				return;
			}

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

					ProcessDropeedRecipes(thisRecipesProp, draggedObjects);
				}
			}
        }

		private void ProcessDropeedRecipes(SerializedProperty thisRecipesProp, Object[] draggedObjects)
		{
            recipesIndexed = false;
			for (int i = 0; i < draggedObjects.Length; i++)
			{
				if (draggedObjects[i])
				{
					UMATextRecipe tempRecipeAsset = draggedObjects[i] as UMATextRecipe;
					if (!tempRecipeAsset)
					{
						var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
						if (System.IO.Directory.Exists(path))
						{
							RecursiveScanFoldersForAssets(path, thisRecipesProp);
						}
					}
					if (tempRecipeAsset && (tempRecipeAsset.recipeType == "Wardrobe" || tempRecipeAsset.recipeType == "WardrobeCollection"))
                    {
                        AddRecipe(thisRecipesProp, tempRecipeAsset);
                        continue;
                    }
                }
			}
            if (recipesIndexed)
            {
                recipesIndexed = false;
                UMAContextBase.Instance.ValidateDictionaries();
            }
		}

        private bool AddRecipe(SerializedProperty thisRecipesProp, UMATextRecipe tempRecipeAsset)
        {
            bool needToAddNew = true;
            for (int ii = 0; ii < thisRecipesProp.arraySize; ii++)
            {
                SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(ii);
                if (thisElement.FindPropertyRelative("_recipeName").stringValue == tempRecipeAsset.name)
                {
                    int compatibleRacesArraySize = tempRecipeAsset.compatibleRaces.Count;
                    thisRecipesProp.GetArrayElementAtIndex(ii).FindPropertyRelative("_compatibleRaces").arraySize = compatibleRacesArraySize;
                    for (int cr = 0; cr < compatibleRacesArraySize; cr++)
                    {
                        thisRecipesProp.GetArrayElementAtIndex(ii).FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue = tempRecipeAsset.compatibleRaces[cr];
                    }
                    needToAddNew = false;
                }
            }
            if (needToAddNew)
            {
                if (!UMAContextBase.Instance.HasRecipe(tempRecipeAsset.name))
                {
                    UMAContextBase.Instance.AddRecipe(tempRecipeAsset);
                    recipesIndexed = true;
                }
                int newArrayElIndex = thisRecipesProp.arraySize;
                thisRecipesProp.InsertArrayElementAtIndex(newArrayElIndex);
                thisRecipesProp.serializedObject.ApplyModifiedProperties();
                thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_recipeName").stringValue = tempRecipeAsset.name;
                int compatibleRacesArraySize = tempRecipeAsset.compatibleRaces.Count;
                thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_compatibleRaces").arraySize = compatibleRacesArraySize;
                for (int cr = 0; cr < compatibleRacesArraySize; cr++)
                {
                    thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue = tempRecipeAsset.compatibleRaces[cr];
                }
                thisRecipesProp.serializedObject.ApplyModifiedProperties();
                GUI.changed = true;
                changed = true;
                return true;
            }
            return false;
        }

        protected void RecursiveScanFoldersForAssets(string path, SerializedProperty thisRecipesProp)
		{
			List<Object> droppedItems = new List<Object>();

			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				var tempRecipe = AssetDatabase.LoadAssetAtPath(assetFile, typeof(UMAWardrobeRecipe)) as UMAWardrobeRecipe;
				if (tempRecipe)
				{
					droppedItems.Add(tempRecipe);
				}
			}
			if (droppedItems.Count > 0)
			{
				ProcessDropeedRecipes(thisRecipesProp, droppedItems.ToArray());
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), thisRecipesProp);
			}
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight + padding;
            int extraLines = 0;
            if (defaultOpen)
            {
                var thisRecipesProp = property.FindPropertyRelative("recipes");
                extraLines = thisRecipesProp.arraySize;
                h *= (extraLines + 3);// add space for button
                h += 50f + padding;
            }
            return h;
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
			changed = false;
            if (warningIcon == null)
            {
                warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
            }
            EditorGUI.BeginProperty(position, label, property);
            var r0 = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);

			defaultOpen = EditorGUI.Foldout(r0, defaultOpen, "Default Wardrobe Recipes");
            if (defaultOpen)
            {
                var valR = r0;
                valR = new Rect(valR.xMin, valR.yMax, valR.width, EditorGUIUtility.singleLineHeight);
				EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(valR, property.FindPropertyRelative("loadDefaultRecipes"));
				if (EditorGUI.EndChangeCheck())
                {
					 property.serializedObject.ApplyModifiedProperties();
				}
                Rect dropArea = new Rect(valR.xMin, (valR.yMax + padding), valR.width, 50f);
                GUI.Box(dropArea, "Drag Wardrobe Recipes here or click to pick");

				// menu/submenus for Slot/RecipeName.
				// Example:
				//  [Head/DragonHelm    ][Add Item]

                valR = new Rect(valR.xMin, (valR.yMin + 50f + padding), valR.width, EditorGUIUtility.singleLineHeight);
                var thisRecipesProp = property.FindPropertyRelative("recipes");
                float textFieldWidth = (valR.width - 20f);
                var warningStyle = new GUIStyle(EditorStyles.label);
                warningStyle.fixedHeight = warningIcon.height + 4f;
                warningStyle.contentOffset = new Vector2(0, -2f);
                //can we make these validate to the compatible races is upto date?
                thisDCA.preloadWardrobeRecipes.GetRecipesForRace();
                for (int i = 0; i < thisRecipesProp.arraySize; i++)
                {
                    var valRBut = new Rect((textFieldWidth + 18f), (valR.yMax + padding), 20f, EditorGUIUtility.singleLineHeight);
                    valR = new Rect(valR.xMin, (valR.yMax + padding), textFieldWidth, EditorGUIUtility.singleLineHeight);
                    SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                    EditorGUI.BeginDisabledGroup(true);
                    int compatibleRacesArraySize = thisElement.FindPropertyRelative("_compatibleRaces").arraySize;
                    string compatibleRaces = "";
                    for (int cr = 0; cr < compatibleRacesArraySize; cr++)
                    {
                        compatibleRaces = compatibleRaces + thisElement.FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue;
                        if (cr < compatibleRacesArraySize - 1)
                        {
                            compatibleRaces = compatibleRaces + ", ";
                        }
                    }
                    var recipeIsLive = true;
                    // var _recipe = thisElement.FindPropertyRelative("_recipe").objectReferenceValue;// as UMATextRecipe;

                    string recipeName = "";
					var recipe = thisDCA.preloadWardrobeRecipes.recipes[i];
                    if (recipe != null)
                    {
                        string recipeslot = "unknown";
                        if (recipe._recipe != null)
                        {
                            recipeslot = recipe._recipe.wardrobeSlot;
                        }
                        recipeName = thisElement.FindPropertyRelative("_recipeName").stringValue;

                        recipeIsLive = UMAContext.Instance.HasRecipe(recipeName);

                        if (!recipeIsLive)
                            valR.width = valR.width - 25f;

                        EditorGUI.TextField(valR, "[" + recipeslot + "] " + recipeName + " (" + compatibleRaces + ")");
                    }
                    else
                    {
                        EditorGUI.TextField(valR, "Recipe is null.");
                    }


                    EditorGUI.EndDisabledGroup();
                    if (!recipeIsLive && recipe != null)
                    {
                        var warningRect = new Rect((valRBut.xMin - 25f), valRBut.yMin, 20f, valRBut.height);
						var warningGUIContent = new GUIContent("", recipeName + " was not Live. Click this button to add it to the Global Library.");
						warningGUIContent.image = warningIcon;
						//show a warning icon if the added recipe is not available from the global index (or assetBundles)
						var foundRecipe = FindMissingRecipe(recipeName);
						if (GUI.Button(warningRect, warningGUIContent, warningStyle))
						{
							//the _recipe value is no longer serialized so we need to get it from AssetDatabase
							if (foundRecipe != null)
								UMAAssetIndexer.Instance.EvilAddAsset(foundRecipe.GetType(), foundRecipe);
						}
					}
                    if (GUI.Button(valRBut, "X"))
                    {
						changed = true;
                        thisRecipesProp.DeleteArrayElementAtIndex(i);
                        thisRecipesProp.serializedObject.ApplyModifiedProperties();
                    }
                }
                DropAreaGUI(dropArea, thisRecipesProp);
            }
            EditorGUI.EndProperty();
        }
        /// <summary>
        /// with wardobeRecipes, DynamicCharacterSystem does not have a list of refrenced recipes like the other libraries
        /// so the only way to get them is from DynamicAssetLoader (which is how DCS gets them) 
        /// so they MUST be in an assetBundle or in Global Index or there is no way of finding them
        /// </summary>
        /// <param name="recipeName"></param>
        /// <returns></returns>
		/// 

		private UMARecipeBase FindMissingRecipe(string recipeName)
		{
			UMARecipeBase foundRecipe = null;
			//the following will find things like femaleHair1 if 'maleHair1' is the recipe name
			var foundWardrobeGUIDS = AssetDatabase.FindAssets("t:UMAWardrobeRecipe " + recipeName);
			if (foundWardrobeGUIDS.Length > 0)
			{
				foreach (string guid in foundWardrobeGUIDS)
				{
					var tempAsset = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(AssetDatabase.GUIDToAssetPath(guid));
					if (tempAsset.name == recipeName)
					{
						foundRecipe = tempAsset;
						break;
					}
				}
			}
			//try searching for WardrobeCollections
			if (foundRecipe == null)
			{
				var foundWardrobeCollectionGUIDS = AssetDatabase.FindAssets("t:UMAWardrobeCollection " + recipeName);
				if (foundWardrobeCollectionGUIDS.Length > 0)
				{
					foreach (string guid in foundWardrobeCollectionGUIDS)
					{
						var tempAsset = AssetDatabase.LoadAssetAtPath<UMAWardrobeCollection>(AssetDatabase.GUIDToAssetPath(guid));
						if (tempAsset.name == recipeName)
						{
							foundRecipe = tempAsset;
							break;
						}
					}
				}
			}
			return foundRecipe;
		}
	}
}
#endif
