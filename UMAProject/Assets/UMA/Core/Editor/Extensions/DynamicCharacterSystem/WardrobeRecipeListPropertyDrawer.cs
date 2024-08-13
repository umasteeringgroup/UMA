#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA.CharacterSystem.Editors
{
    [CustomPropertyDrawer(typeof(DynamicCharacterAvatar.WardrobeRecipeList))]
    public class WardrobeRecipeListPropertyDrawer : PropertyDrawer
    {
        public List<string> recipes = new List<string>();
        public List<string> recipeMenu = new List<string>();
        public string LastRace = "";
        public static int lastAdded = -1;
        public static int selectedSlotIndex = 0;


        // float padding = 2f;
        // public DynamicCharacterSystem thisDCS;
        public DynamicCharacterAvatar thisDCA;
		public bool changed = false;
		static bool defaultOpen = true;
        Texture warningIcon;
		int wardrobeRecipePickerID = -1;
        bool recipesIndexed = false;
        public static bool ShowOnlyCompatibleRecipes = false;
        public static bool ShowOnlySelectedSlot = false;
        public static bool ShowOnlyActive = false;
        public static bool ToggleAll = false;

        public void SetupDropdown(string race)
        {
            if (LastRace != race) 
            {
                LastRace = race;
                recipes.Clear();
                recipeMenu.Clear();
                if (thisDCA != null)
                {
                    var availableRecipes = thisDCA.AvailableRecipes;
                    foreach (var slot in availableRecipes.Keys)
                    {
                        foreach (var recipe in availableRecipes[slot])
                        {
                            recipes.Add(recipe.name);
                            recipeMenu.Add(slot + "/" + recipe.name);
                        }
                    }
                }
            }
        }

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
                {
                    Event.current.Use();//stops the Mismatched LayoutGroup errors
                }

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
                thisRecipesProp.GetArrayElementAtIndex(newArrayElIndex).FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = true;
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
            for (int i = 0; i < assetFiles.Length; i++)
			{
                string assetFile = assetFiles[i];
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
            string[] array = System.IO.Directory.GetDirectories(path);
            for (int i = 0; i < array.Length; i++)
			{
                string subFolder = array[i];
                RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), thisRecipesProp);
			}
		}

        /*public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
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
        } */

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return 0;
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
			changed = false;
            if (warningIcon == null)
            {
                warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
            }
            EditorGUI.BeginProperty(position, label, property);
            //var r0 = new Rect(position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);

			defaultOpen = EditorGUILayout.Foldout(defaultOpen, "Default Wardrobe Recipes");
            if (defaultOpen)
            {
                //var valR = r0;
                //valR = new Rect(valR.xMin, valR.yMax, valR.width, EditorGUIUtility.singleLineHeight);
				EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(property.FindPropertyRelative("loadDefaultRecipes"));
				if (EditorGUI.EndChangeCheck())
                {
					 property.serializedObject.ApplyModifiedProperties();
				}
                //Rect dropArea = new Rect(valR.xMin, (valR.yMax + padding), valR.width, 50f);
                GUILayout.Box("Drag Wardrobe Recipes here or click to pick",GUILayout.Height(50),GUILayout.ExpandWidth(true));
                Rect dropArea = GUILayoutUtility.GetLastRect();
                //GUI.Box(dropArea, "Drag Wardrobe Recipes here or click to pick");

				// menu/submenus for Slot/RecipeName.
				// Example:
				//  [Head/DragonHelm    ][Add Item]

               // valR = new Rect(valR.xMin, (valR.yMin + 50f + padding), valR.width, EditorGUIUtility.singleLineHeight);
                var thisRecipesProp = property.FindPropertyRelative("recipes");
                //float textFieldWidth = (valR.width - 20f);
                var warningStyle = new GUIStyle(EditorStyles.label);
                warningStyle.fixedHeight = warningIcon.height + 4f;
                warningStyle.contentOffset = new Vector2(0, -2f);
                //can we make these validate to the compatible races is upto date?
                thisDCA.preloadWardrobeRecipes.GetRecipesForRace();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Enable All"))
                {
                    for (int i = 0; i < thisRecipesProp.arraySize; i++)
                    {
                        SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                        thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = true;
                        changed = true;
                    }
                }
                if (GUILayout.Button("Disable All"))
                {
                    for (int i = 0; i < thisRecipesProp.arraySize; i++)
                    {
                        SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                        thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = false;
                        changed = true;
                    }
                }
                if (GUILayout.Button("Add all"))
                {
                    var availableRecipes = thisDCA.AvailableRecipes;
                    foreach (var slot in availableRecipes.Keys)
                    {
                        foreach (var recipe in availableRecipes[slot])
                        {
                            var recipeAsset = UMAContextBase.Instance.GetRecipe(recipe.name, false);
                            if (recipeAsset != null)
                            {
                                AddRecipe(thisRecipesProp, recipeAsset);
                            }
                        }
                    }
                }
                if (GUILayout.Button("Remove disabled"))
                {
                    RemoveDisabled(thisRecipesProp);
                    changed = true;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();

                if (GUILayout.Toggle(ShowOnlyActive, "Active Only", GUILayout.Width(100)))
                {
                    ShowOnlyActive = true;
                }
                else
                {
                    ShowOnlyActive = false;
                }

                if (GUILayout.Toggle(ShowOnlyCompatibleRecipes, "Compatible Only", GUILayout.ExpandWidth(true)))
                {
                    ShowOnlyCompatibleRecipes = true;
                }
                else
                {
                    ShowOnlyCompatibleRecipes = false;
                }


                //if (GUILayout.Toggle(ShowOnlySelectedSlot, "Selected WardrobeSlot", GUILayout.ExpandWidth(true)))
                //{
                //    ShowOnlySelectedSlot = true;
                //}
                //else
                //{
                //    ShowOnlySelectedSlot = false;
                //}

                string selectedSlot = "";

                if (thisDCA.activeRace == null || thisDCA.activeRace.data == null)
                {
                    ShowOnlySelectedSlot = false;
                    EditorGUILayout.LabelField("Race is not set", GUILayout.Width(120));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    if (selectedSlotIndex >= thisDCA.activeRace.data.wardrobeSlots.Count)
                    {
                        selectedSlotIndex = 0;
                    }
                    GUILayout.Label("Wardrobe Slot", GUILayout.Width(85));
                    selectedSlotIndex = EditorGUILayout.Popup(selectedSlotIndex, thisDCA.activeRace.data.wardrobeSlots.ToArray(), GUILayout.Width(120));
                    if (selectedSlotIndex >= 0 && selectedSlotIndex < thisDCA.activeRace.data.wardrobeSlots.Count)
                    {
                        selectedSlot = thisDCA.activeRace.data.wardrobeSlots[selectedSlotIndex];
                    }


                    if (selectedSlotIndex == 0)
                    {
                        ShowOnlySelectedSlot = false;
                    }
                    else
                    {
                        ShowOnlySelectedSlot = true;
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    SetupDropdown(thisDCA.activeRace.name);

                    ToggleAll = GUILayout.Toggle(ToggleAll, "Toggle", GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Sort by Slot", GUILayout.Width(100)))
                    {
                        SortBySlot(thisRecipesProp);
                    }
                   
                    int added = -1;
                    EditorGUILayout.LabelField("Add Item", GUILayout.Width(60));
                    added = EditorGUILayout.Popup(added, recipeMenu.ToArray(), GUILayout.Width(150));
                    if (added >= 0)
                    {
                        var recipe = recipes[added];
                        var recipeAsset = UMAContextBase.Instance.GetRecipe(recipe, false);
                        if (recipeAsset != null)
                        {
                            AddRecipe(thisRecipesProp, recipeAsset);
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                for (int i = 0; i < thisRecipesProp.arraySize; i++)
                {
                    string currentSlot = "";
                    bool compatible = false;
                   // var valRBut = new Rect((textFieldWidth + 18f), (valR.yMax + padding), 20f, EditorGUIUtility.singleLineHeight);
                   // valR = new Rect(valR.xMin, (valR.yMax + padding), textFieldWidth, EditorGUIUtility.singleLineHeight);
                    SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);

                    //                    UMAWardrobeRecipe currentRecipe = thisElement.objectReferenceValue as UMAWardrobeRecipe;
                    var recipeListItem = thisDCA.preloadWardrobeRecipes.recipes[i];

                    var currentRecipe = thisDCA.preloadWardrobeRecipes.recipes[i]._recipe;
                    if (ShowOnlySelectedSlot)
                    {
                        if (currentRecipe != null)
                        {
                            currentSlot = currentRecipe.wardrobeSlot;
                            if (currentSlot != selectedSlot)
                            {
                                continue;
                            }
                        }
                    }

                    if (ShowOnlyActive)
                    {
                        if (currentRecipe != null)
                        {
                            if (!recipeListItem._enabledInDefaultWardrobe)
                            {
                                continue;
                            }
                        }
                    }
                    int compatibleRacesArraySize = thisElement.FindPropertyRelative("_compatibleRaces").arraySize;
                    string compatibleRaces = "";
                    for (int cr = 0; cr < compatibleRacesArraySize; cr++)
                    {
                        string race = thisElement.FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue;
                        compatibleRaces = compatibleRaces + race;
                        if (thisDCA.activeRace != null)
                        {
                            if (thisDCA.activeRace.data != null)
                            {
                                if (thisDCA.activeRace.data.IsCrossCompatibleWith(race))
                                {
                                    compatible = true;
                                }
                                if (race == thisDCA.activeRace.name)
                                {
                                    compatible = true;
                                }

                                if (cr < compatibleRacesArraySize - 1)
                                {
                                    compatibleRaces = compatibleRaces + ", ";
                                }
                            }
                        }
                    }

                    if (ShowOnlyCompatibleRecipes && compatible == false)
                    {
                        continue;
                    }

                    GUILayout.BeginHorizontal();

                    var recipeIsLive = true;
                    // var _recipe = thisElement.FindPropertyRelative("_recipe").objectReferenceValue;// as UMATextRecipe;

                    string recipeName = "";
                    if (recipeListItem != null)
                    {
                        string recipeslot = "unknown";
                        if (recipeListItem._recipe != null)
                        {
                            recipeslot = recipeListItem._recipe.wardrobeSlot;
                        }
                        recipeName = thisElement.FindPropertyRelative("_recipeName").stringValue;

                        if (UMAContext.Instance != null)
                        {
                            recipeIsLive = UMAContext.Instance.HasRecipe(recipeName);
                        }

                        string prequel = "";

                       if (recipeListItem._enabledInDefaultWardrobe)
                        {
                            EditorGUI.BeginDisabledGroup(false);
                            prequel = "+";
                            var currentWardrobe = thisDCA.WardrobeRecipes;
                            var values = currentWardrobe.Values;
                            foreach (var rcp in values)
                            {
                                if (rcp.name == recipeName)
                                {
                                    prequel = "*";
                                    break;
                                }
                            }
                        }
                        else
                        {
                            prequel = "-";
                            EditorGUI.BeginDisabledGroup(true);
                        }
                        EditorGUILayout.TextField($"{prequel}[{recipeslot}] { recipeName}  ({ compatibleRaces} )",GUILayout.ExpandWidth(true));
                    }
                    else
                    {
                        EditorGUILayout.TextField("Recipe is null.", GUILayout.ExpandWidth(true));
                    }


                    EditorGUI.EndDisabledGroup();
                    if (!recipeIsLive && recipeListItem != null)
                    {
                        //var warningRect = new Rect((valRBut.xMin - 25f), valRBut.yMin, 20f, valRBut.height);
						var warningGUIContent = new GUIContent("", recipeName + " was not Live. Click this button to add it to the Global Library.");
						warningGUIContent.image = warningIcon;
						//show a warning icon if the added recipe is not available from the global index (or assetBundles)
						var foundRecipe = FindMissingRecipe(recipeName);
						if (GUILayout.Button(warningGUIContent, warningStyle))
						{
							//the _recipe value is no longer serialized so we need to get it from AssetDatabase
							if (foundRecipe != null)
                            {
                                UMAAssetIndexer.Instance.EvilAddAsset(foundRecipe.GetType(), foundRecipe);
                            }
                        }
					}
                    if (GUILayout.Button("0/1",GUILayout.Width(30)))
                    {
                        if (recipeListItem._enabledInDefaultWardrobe)
                        {
                            recipeListItem._enabledInDefaultWardrobe = false;
                        }
                        else
                        {
                            if (ToggleAll)
                            {
                                string wardrobeSlot = recipeListItem._recipe.wardrobeSlot;
                                for (int j = 0; j < thisRecipesProp.arraySize; j++)
                                {
                                    SerializedProperty thisElement2 = thisRecipesProp.GetArrayElementAtIndex(j);
                                    var toggleRecipe = thisDCA.preloadWardrobeRecipes.recipes[j];
                                    if (toggleRecipe._recipe.wardrobeSlot == wardrobeSlot)
                                    {
                                        toggleRecipe._enabledInDefaultWardrobe = false ;
                                    }
                                }
                            }
                            recipeListItem._enabledInDefaultWardrobe = true;
                        }
                        changed = true;
                        thisRecipesProp.serializedObject.Update();
                    }

                    if (recipeListItem._recipe != null)
                    {
                        if (GUILayout.Button("Ping", GUILayout.Width(40)))
                        {
                            EditorGUIUtility.PingObject(recipeListItem._recipe);
                        }
                        if (GUILayout.Button("Insp", GUILayout.Width(40)))
                        {
                            InspectorUtlity.InspectTarget(recipeListItem._recipe);
                        }
                    }
                    if (GUILayout.Button("x", GUILayout.Width(15)))
                    {
						changed = true;
                        thisRecipesProp.DeleteArrayElementAtIndex(i);
                        thisRecipesProp.serializedObject.ApplyModifiedProperties();
                    }
                    GUILayout.EndHorizontal();
                }
                DropAreaGUI(dropArea, thisRecipesProp);
            }
           EditorGUI.EndProperty();
        }

        private void SortBySlot(SerializedProperty thisRecipesProp)
        {
            // Sort the list by slot
            List<DynamicCharacterAvatar.WardrobeRecipeListItem> sortedList = new List<DynamicCharacterAvatar.WardrobeRecipeListItem>();
            for (int i = 0; i < thisRecipesProp.arraySize; i++)
            {
               sortedList.Add(thisDCA.preloadWardrobeRecipes.recipes[i]);
            }

            sortedList.Sort((x, y) => x._recipe.wardrobeSlot.CompareTo(y._recipe.wardrobeSlot));
            thisDCA.preloadWardrobeRecipes.recipes = sortedList;
            changed = true;
            thisRecipesProp.serializedObject.Update();
        }

        private void RemoveDisabled(SerializedProperty thisRecipesProp)
        {
            // For each recipe in the list, if it is disabled, remove it.
            for (int i = thisRecipesProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                if (!thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue)
                {
                    thisRecipesProp.DeleteArrayElementAtIndex(i);
                    changed = true;
                }
            }
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
                for (int i = 0; i < foundWardrobeGUIDS.Length; i++)
				{
                    string guid = foundWardrobeGUIDS[i];
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
                    for (int i = 0; i < foundWardrobeCollectionGUIDS.Length; i++)
					{
                        string guid = foundWardrobeCollectionGUIDS[i];
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
