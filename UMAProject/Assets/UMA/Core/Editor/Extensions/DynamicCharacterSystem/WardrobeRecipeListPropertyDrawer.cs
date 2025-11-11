#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

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

        private static bool IsEditorBusy => EditorApplication.isCompiling || EditorApplication.isUpdating;

        private static UMAAssetIndexer TryGetIndexer()
        {
            try { return UMAAssetIndexer.Instance; }
            catch { return null; }
        }

        private void ScheduleRebuildRaceRecipes()
        {
            // Schedule when editor is idle and indexer is available
            EditorApplication.delayCall += () =>
            {
                if (IsEditorBusy) { ScheduleRebuildRaceRecipes(); return; }
                var idx = TryGetIndexer();
                if (idx == null) { ScheduleRebuildRaceRecipes(); return; }
                try { idx.RebuildRaceRecipes(); } catch { }
            };
        }

        private void EnsureDCA(SerializedProperty property)
        {
            if (thisDCA == null)
            {
                try
                {
                    thisDCA = property?.serializedObject?.targetObject as DynamicCharacterAvatar;
                }
                catch { thisDCA = null; }
            }
        }

        public void SetupDropdown(string race)
        {
            if (LastRace != race)
            {
                LastRace = race;
                recipes.Clear();
                recipeMenu.Clear();
                if (thisDCA != null)
                {
                    try
                    {
                        var availableRecipes = thisDCA.AvailableRecipes;
                        if (availableRecipes != null)
                        {
                            foreach (var slot in availableRecipes.Keys)
                            {
                                var list = availableRecipes[slot];
                                if (list == null) continue;
                                foreach (var recipe in list)
                                {
                                    if (recipe == null) continue;
                                    recipes.Add(recipe.name);
                                    recipeMenu.Add(slot + "/" + recipe.name);
                                }
                            }
                        }
                    }
                    catch { /* ignore during reload */ }
                }
            }
        }

        //Make a drop area for wardrobe recipes
        private void DropAreaGUI(Rect dropArea, SerializedProperty thisRecipesProp)
        {
            if (thisRecipesProp == null) return;
            var evt = Event.current;
            // Click-to-pick
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
                if (IsEditorBusy) return;
                UMAWardrobeRecipe uwr = EditorGUIUtility.GetObjectPickerObject() as UMAWardrobeRecipe;
                recipesIndexed = false;
                if (uwr != null && AddRecipe(thisRecipesProp, uwr))
                {
                    if (recipesIndexed)
                    {
                        recipesIndexed = false;
                        var idx = TryGetIndexer();
                        if (idx != null) { try { idx.RebuildRaceRecipes(); } catch { ScheduleRebuildRaceRecipes(); } }
                        else { ScheduleRebuildRaceRecipes(); }
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

        private void ProcessDropeedRecipes(SerializedProperty thisRecipesProp, UnityEngine.Object[] draggedObjects)
        {
            if (thisRecipesProp == null || draggedObjects == null) return;
            if (IsEditorBusy) return;

            recipesIndexed = false;
            for (int i = 0; i < draggedObjects.Length; i++)
            {
                if (!draggedObjects[i]) continue;

                var tempRecipeAsset = draggedObjects[i] as UMATextRecipe;
                if (tempRecipeAsset == null)
                {
                    var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
                    if (System.IO.Directory.Exists(path))
                    {
                        RecursiveScanFoldersForAssets(path, thisRecipesProp);
                    }
                    continue;
                }

                if (tempRecipeAsset.recipeType == "Wardrobe" || tempRecipeAsset.recipeType == "WardrobeCollection")
                {
                    AddRecipe(thisRecipesProp, tempRecipeAsset);
                }
            }
            if (recipesIndexed)
            {
                recipesIndexed = false;
                var idx = TryGetIndexer();
                if (idx != null) { try { idx.RebuildRaceRecipes(); } catch { ScheduleRebuildRaceRecipes(); } }
                else { ScheduleRebuildRaceRecipes(); }
            }
        }

        private bool AddRecipe(SerializedProperty thisRecipesProp, UMATextRecipe tempRecipeAsset)
        {
            if (thisRecipesProp == null || tempRecipeAsset == null) return false;

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
                var idx = TryGetIndexer();
                if (idx != null)
                {
                    try
                    {
                        if (!idx.HasRecipe(tempRecipeAsset.name))
                        {
                            idx.AddRecipe(tempRecipeAsset);
                            recipesIndexed = true;
                        }
                    }
                    catch { /* indexer might be mid-reload */ }
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
            if (string.IsNullOrEmpty(path)) return;
            List<UnityEngine.Object> droppedItems = new List<UnityEngine.Object>();

            string[] assetFiles;
            try { assetFiles = System.IO.Directory.GetFiles(path, "*.asset"); }
            catch { return; }

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
            string[] subDirs;
            try { subDirs = System.IO.Directory.GetDirectories(path); }
            catch { return; }

            for (int i = 0; i < subDirs.Length; i++)
            {
                string subFolder = subDirs[i];
                RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), thisRecipesProp);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Layout is handled by GUILayout; returning 0 keeps the drawer compact.
            return 0;
        }

        private UMATextRecipe SafeGetRecipeByName(string recipeName, int indexFromDca)
        {
            // Prefer the live DCA list if available and aligned
            try
            {
                if (thisDCA?.preloadWardrobeRecipes?.recipes != null &&
                    indexFromDca >= 0 && indexFromDca < thisDCA.preloadWardrobeRecipes.recipes.Count)
                {
                    var item = thisDCA.preloadWardrobeRecipes.recipes[indexFromDca];
                    if (item != null && item._recipe != null) return item._recipe;
                }
            }
            catch { }

            // Fallback to indexer by name
            var idx = TryGetIndexer();
            if (idx != null && !string.IsNullOrEmpty(recipeName))
            {
                try { return idx.GetRecipe(recipeName, false); } catch { }
            }
            return null;
        }

        private bool IndexerHasRecipe(string recipeName)
        {
            var idx = TryGetIndexer();
            if (idx == null || string.IsNullOrEmpty(recipeName)) return false;
            try { return idx.HasRecipe(recipeName); } catch { return false; }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsEditorBusy)
            {
                return;
            }

            if (warningIcon == null)
            {
                warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
            }

            changed = false;
            EnsureDCA(property);

            EditorGUI.BeginProperty(position, label, property);

            defaultOpen = EditorGUILayout.Foldout(defaultOpen, "Default Wardrobe Recipes");
            if (defaultOpen)
            {
                UMA.Editors.GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                // Attempt to refresh live race recipes safely
                try
                {
                    if (thisDCA?.preloadWardrobeRecipes != null)
                    {
                        thisDCA.preloadWardrobeRecipes.GetRecipesForRace();
                    }
                }
                catch { }

                var thisRecipesProp = property.FindPropertyRelative("recipes");
                if (thisRecipesProp == null)
                {
                    EditorGUILayout.HelpBox("Recipes list not found or not serialized.", MessageType.Info);
                    EditorGUI.EndProperty();
                    return;
                }

                GUILayout.Box("Drag Wardrobe Recipes here or click to pick", GUILayout.Height(50), GUILayout.ExpandWidth(true));
                Rect dropArea = GUILayoutUtility.GetLastRect();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Enable All"))
                {
                    for (int i = 0; i < thisRecipesProp.arraySize; i++)
                    {
                        SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                        thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = true;
                        changed = true;
                    }
                    // Commit changes
                    thisRecipesProp.serializedObject.ApplyModifiedProperties();
                }
                if (GUILayout.Button("Disable All"))
                {
                    for (int i = 0; i < thisRecipesProp.arraySize; i++)
                    {
                        SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                        thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = false;
                        changed = true;
                    }
                    // Commit changes
                    thisRecipesProp.serializedObject.ApplyModifiedProperties();
                }
                if (thisDCA != null && GUILayout.Button("Add all"))
                {
                    try
                    {
                        var availableRecipes = thisDCA.AvailableRecipes;
                        if (availableRecipes != null)
                        {
                            foreach (var slot in availableRecipes.Keys)
                            {
                                var list = availableRecipes[slot];
                                if (list == null) continue;
                                foreach (var recipe in list)
                                {
                                    if (recipe == null) continue;
                                    var recipeAsset = TryGetIndexer()?.GetRecipe(recipe.name, false);
                                    if (recipeAsset != null)
                                    {
                                        AddRecipe(thisRecipesProp, recipeAsset);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                if (GUILayout.Button("Remove disabled"))
                {
                    RemoveDisabled(thisRecipesProp);
                    changed = true;
                    // Commit changes
                    thisRecipesProp.serializedObject.ApplyModifiedProperties();
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                ShowOnlyActive = GUILayout.Toggle(ShowOnlyActive, "Active Only", GUILayout.Width(100));
                ShowOnlyCompatibleRecipes = GUILayout.Toggle(ShowOnlyCompatibleRecipes, "Compatible Only", GUILayout.ExpandWidth(true));

                string selectedSlot = "";
                bool hasRace = thisDCA?.activeRace != null && thisDCA.activeRace.data != null;

                if (!hasRace)
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
                    ShowOnlySelectedSlot = selectedSlotIndex != 0;
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
                        var recipeAsset = TryGetIndexer()?.GetRecipe(recipe, false);
                        if (recipeAsset != null)
                        {
                            AddRecipe(thisRecipesProp, recipeAsset);
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                for (int i = 0; i < thisRecipesProp.arraySize; i++)
                {
                    SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                    if (thisElement == null) continue;

                    string recipeName = thisElement.FindPropertyRelative("_recipeName").stringValue;

                    var recipeAsset = SafeGetRecipeByName(recipeName, i);
                    string recipeslot = recipeAsset != null ? recipeAsset.wardrobeSlot : "unknown";

                    if (ShowOnlySelectedSlot && !string.IsNullOrEmpty(recipeslot) && hasRace)
                    {
                        if (recipeslot != selectedSlot) continue;
                    }

                    bool compatible = false;
                    int compatibleRacesArraySize = thisElement.FindPropertyRelative("_compatibleRaces").arraySize;
                    string compatibleRaces = "";
                    for (int cr = 0; cr < compatibleRacesArraySize; cr++)
                    {
                        string race = thisElement.FindPropertyRelative("_compatibleRaces").GetArrayElementAtIndex(cr).stringValue;
                        if (!string.IsNullOrEmpty(race))
                        {
                            compatibleRaces += race;
                        }
                        if (hasRace)
                        {
                            try
                            {
                                if (thisDCA.activeRace.data.IsCrossCompatibleWith(race) || race == thisDCA.activeRace.name)
                                {
                                    compatible = true;
                                }
                            }
                            catch { }
                        }
                        if (cr < compatibleRacesArraySize - 1) compatibleRaces += ", ";
                    }
                    if (ShowOnlyCompatibleRecipes && compatible == false) continue;

                    GUILayout.BeginHorizontal();

                    bool enabledInDefault = thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue;

                    string prequel = enabledInDefault ? "+" : "-";

                    if (enabledInDefault && thisDCA != null)
                    {
                        try
                        {
                            var currentWardrobe = thisDCA.WardrobeRecipes;
                            if (currentWardrobe != null)
                            {
                                foreach (var rcp in currentWardrobe.Values)
                                {
                                    if (rcp != null && rcp.name == recipeName)
                                    {
                                        prequel = "*";
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    bool canToggleForce = false;
                    bool forceLoad = false;
                    try
                    {
                        if (thisDCA?.preloadWardrobeRecipes?.recipes != null &&
                            i >= 0 && i < thisDCA.preloadWardrobeRecipes.recipes.Count &&
                            thisDCA.preloadWardrobeRecipes.recipes[i] != null)
                        {
                            canToggleForce = true;
                            forceLoad = thisDCA.preloadWardrobeRecipes.recipes[i].ForceLoad;
                            if (forceLoad) prequel += "F";
                        }
                    }
                    catch { }

                    EditorGUI.BeginDisabledGroup(!enabledInDefault);
                    EditorGUILayout.TextField($"{prequel}[{recipeslot}] {recipeName}  ({compatibleRaces})", GUILayout.ExpandWidth(true));
                    EditorGUI.EndDisabledGroup();

                    bool recipeIsLive = IndexerHasRecipe(recipeName);
                    if (!recipeIsLive)
                    {
                        var warningGUIContent = new GUIContent("", recipeName + " was not Live. Click this button to add it to the Global Library.");
                        warningGUIContent.image = warningIcon;
                        if (GUILayout.Button(warningGUIContent, GUILayout.Width(20)))
                        {
                            var foundRecipe = FindMissingRecipe(recipeName);
                            var idx = TryGetIndexer();
                            if (foundRecipe != null && idx != null)
                            {
                                try { idx.EvilAddAsset(foundRecipe.GetType(), foundRecipe); } catch { }
                            }
                        }
                    }

                    // Toggle enabled
                    if (GUILayout.Button("0/1", GUILayout.Width(30)))
                    {
                        bool newValue = !enabledInDefault;

                        // ToggleAll by same wardrobe slot when enabling
                        if (newValue && ToggleAll && recipeAsset != null)
                        {
                            string wardrobeSlot = recipeAsset.wardrobeSlot;
                            for (int j = 0; j < thisRecipesProp.arraySize; j++)
                            {
                                SerializedProperty other = thisRecipesProp.GetArrayElementAtIndex(j);
                                string otherName = other.FindPropertyRelative("_recipeName").stringValue;
                                var otherAsset = SafeGetRecipeByName(otherName, j);
                                if (otherAsset != null && otherAsset.wardrobeSlot == wardrobeSlot)
                                {
                                    other.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = false;
                                }
                            }
                        }

                        thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = newValue;
                        changed = true;

                        // Commit changes immediately so the toggle sticks
                        thisRecipesProp.serializedObject.ApplyModifiedProperties();
                    }

                    if (recipeAsset != null)
                    {
                        if (GUILayout.Button("Ping", GUILayout.Width(40)))
                        {
                            EditorGUIUtility.PingObject(recipeAsset);
                        }
                        if (GUILayout.Button("Insp", GUILayout.Width(40)))
                        {
                            // Defer popup to the next editor tick to avoid layout errors from drawers
                            var toInspect = recipeAsset;
                            EditorApplication.delayCall += () =>
                            {
                                try { InspectorUtlity.InspectTarget(toInspect); }
                                catch (Exception ex) { Debug.LogException(ex); }
                            };
                            GUIUtility.ExitGUI();
                        }
                        using (new EditorGUI.DisabledScope(!canToggleForce))
                        {
                            if (GUILayout.Button("Force", GUILayout.Width(48)) && canToggleForce)
                            {
                                try
                                {
                                    var item = thisDCA.preloadWardrobeRecipes.recipes[i];
                                    item.ForceLoad = !item.ForceLoad;
                                    // ForceLoad is runtime-only; no Apply needed here
                                    changed = true;
                                }
                                catch { }
                            }
                        }
                    }

                    if (GUILayout.Button("x", GUILayout.Width(15)))
                    {
                        changed = true;
                        thisRecipesProp.DeleteArrayElementAtIndex(i);
                        thisRecipesProp.serializedObject.ApplyModifiedProperties();
                        GUILayout.EndHorizontal();
                        continue;
                    }
                    GUILayout.EndHorizontal();
                }

                DropAreaGUI(dropArea, thisRecipesProp);
                UMA.Editors.GUIHelper.EndVerticalPadded(10);
            }

            try
            {
                EditorGUI.EndProperty();
            }
            catch (System.Exception e)
            {
                Debug.LogError("EditorApplication.isCompiling: " + EditorApplication.isCompiling);
                Debug.LogError("EditorApplication.isUpdating: " + EditorApplication.isUpdating);
                Debug.LogError("EditorApplication.isPlaying: " + EditorApplication.isPlaying);
                Debug.LogError("EditorApplication.isPaused: " + EditorApplication.isPaused);
                Debug.LogError("EditorApplication.isPlayingOrWillChangePlaymode: " + EditorApplication.isPlayingOrWillChangePlaymode);
                Debug.LogException(e);
            }
        }

       
        private void SortBySlot(SerializedProperty thisRecipesProp)
        {
            if (thisDCA?.preloadWardrobeRecipes?.recipes == null || thisRecipesProp == null) return;
            try
            {
                List<DynamicCharacterAvatar.WardrobeRecipeListItem> sortedList = new List<DynamicCharacterAvatar.WardrobeRecipeListItem>();
                for (int i = 0; i < thisRecipesProp.arraySize; i++)
                {
                    if (i < thisDCA.preloadWardrobeRecipes.recipes.Count)
                        sortedList.Add(thisDCA.preloadWardrobeRecipes.recipes[i]);
                }
                sortedList.Sort((x, y) =>
                {
                    string a = x?._recipe != null ? x._recipe.wardrobeSlot : "";
                    string b = y?._recipe != null ? y._recipe.wardrobeSlot : "";
                    return string.Compare(a, b, StringComparison.Ordinal);
                });
                thisDCA.preloadWardrobeRecipes.recipes = sortedList;
                changed = true;
                thisRecipesProp.serializedObject.Update();
            }
            catch { }
        }

        private void RemoveDisabled(SerializedProperty thisRecipesProp)
        {
            if (thisRecipesProp == null) return;
            for (int i = thisRecipesProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty thisElement = thisRecipesProp.GetArrayElementAtIndex(i);
                if (thisElement == null) continue;
                if (!thisElement.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue)
                {
                    thisRecipesProp.DeleteArrayElementAtIndex(i);
                    changed = true;
                }
            }
        }

        private UMARecipeBase FindMissingRecipe(string recipeName)
        {
            UMARecipeBase foundRecipe = null;
            if (string.IsNullOrEmpty(recipeName)) return null;

            try
            {
                var foundWardrobeGUIDS = AssetDatabase.FindAssets("t:UMAWardrobeRecipe " + recipeName);
                if (foundWardrobeGUIDS.Length > 0)
                {
                    for (int i = 0; i < foundWardrobeGUIDS.Length; i++)
                    {
                        string guid = foundWardrobeGUIDS[i];
                        var tempAsset = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                        if (tempAsset != null && tempAsset.name == recipeName)
                        {
                            foundRecipe = tempAsset;
                            break;
                        }
                    }
                }
                //try collections
                if (foundRecipe == null)
                {
                    var foundWardrobeCollectionGUIDS = AssetDatabase.FindAssets("t:UMAWardrobeCollection " + recipeName);
                    if (foundWardrobeCollectionGUIDS.Length > 0)
                    {
                        for (int i = 0; i < foundWardrobeCollectionGUIDS.Length; i++)
                        {
                            string guid = foundWardrobeCollectionGUIDS[i];
                            var tempAsset = AssetDatabase.LoadAssetAtPath<UMAWardrobeCollection>(AssetDatabase.GUIDToAssetPath(guid));
                            if (tempAsset != null && tempAsset.name == recipeName)
                            {
                                foundRecipe = tempAsset;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
            return foundRecipe;
        }
    }
}
#endif
