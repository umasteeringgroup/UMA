#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class SlotMasterEditor
    {
        public static string LastSlot = "";
        public static Dictionary<string, bool> OpenSlots = new Dictionary<string, bool>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInitializeOnLoad()
        {
            LastSlot = "";
            OpenSlots = new Dictionary<string, bool>();
        }

        protected readonly UMAData.UMARecipe _recipe;
        protected readonly UnityEngine.Object _recipeContext;
        internal bool IsEditing(UMAData.UMARecipe recipe) => ReferenceEquals(_recipe, recipe);
        protected readonly List<SlotEditor> _slotEditors = new List<SlotEditor>();
        protected readonly SharedColorsCollectionEditor _sharedColorsEditor = new SharedColorsCollectionEditor();
        private readonly IndexedRecipeAssetPicker<SlotDataAsset> dropSlotPicker = new IndexedRecipeAssetPicker<SlotDataAsset>();
        private readonly IndexedRecipeAssetPicker<SlotDataAsset> slotPicker = new IndexedRecipeAssetPicker<SlotDataAsset>();

        protected List<SlotDataAsset> DraggedSlots = new List<SlotDataAsset>();
        protected List<OverlayDataAsset> DraggedOverlays = new List<OverlayDataAsset>();

        /// <returns>Whether anything was added (a drop of refused assets only changes nothing).</returns>
        protected bool AddDraggedFiles()
        {
            SlotData FirstSlot = null;

            // The same check as the indexed pickers: the recipe stores each asset's UMA name, and a reload
            // finds whatever the Global Library has under that name. Another asset with the same name (or
            // one not in the library) looked right until the recipe was reopened.
            List<string> refused = new List<string>();
            DraggedSlots.RemoveAll(s =>
            {
                if (IndexedRecipeAssetPicker<SlotDataAsset>.IsIndexed(s)) return false;
                refused.Add(s != null ? s.name : "(missing slot)");
                return true;
            });
            bool slotRefused = refused.Count > 0;
            List<string> leftWithSlot = new List<string>();
            DraggedOverlays.RemoveAll(o =>
            {
                // Overlays dropped with a slot that was refused were meant for that slot: without it they
                // would go on whatever slot comes first in the recipe.
                if (!IndexedRecipeAssetPicker<OverlayDataAsset>.IsIndexed(o))
                {
                    refused.Add(o != null ? o.name : "(missing overlay)");
                    return true;
                }
                if (slotRefused)
                {
                    leftWithSlot.Add(o.name);
                    return true;
                }
                return false;
            });
            if (refused.Count > 0)
            {
                Debug.LogWarning("Not added to the recipe: " + string.Join(", ", refused) + ". The UMA Global Library does not have " +
                    (refused.Count == 1 ? "this asset" : "these assets") + " under " + (refused.Count == 1 ? "its" : "their") +
                    " name (it has none, or a different asset with that name), and that is what the recipe would load. Add them to the library first." +
                    (leftWithSlot.Count > 0 ? " Also not added, as they were dropped with that slot: " + string.Join(", ", leftWithSlot) + "." : ""));
            }

            if (DraggedSlots.Count >= 1 && DraggedOverlays.Count == 1)
            {
                foreach (SlotDataAsset sd in DraggedSlots)
                {
                    SlotData slot = new SlotData(sd);
                    slot.AddOverlay(new OverlayData(DraggedOverlays[0]));
                    slot = _recipe.MergeSlot(slot, false); 
                }
                // Cleared like the other drops, or the next drop would add these again.
                DraggedSlots.Clear();
                DraggedOverlays.Clear();
                return true;
            }

            bool added = DraggedSlots.Count > 0;
            foreach (SlotDataAsset sd in DraggedSlots)
            {
                SlotData slot = new SlotData(sd);
                slot = _recipe.MergeSlot(slot, false);
                if (FirstSlot == null)
                {
                    FirstSlot = slot;
                }
            }
            DraggedSlots.Clear();

            if (DraggedOverlays.Count > 0)
            {
                if (FirstSlot == null)
                {
                    foreach (SlotData sd in _recipe.slotDataList)
                    {
                        if (sd != null)
                        {
                            FirstSlot = sd;
                            break;
                        }
                    }
                }

                if (FirstSlot != null)
                {
                    foreach (OverlayDataAsset od in DraggedOverlays)
                    {
                        FirstSlot.AddOverlay(new OverlayData(od));
                    }
                    added = true;
                }
                else
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.LogWarning("No slot found to apply overlay!");
                    }
                }
                DraggedOverlays.Clear();
            }
            return added;
        }

        protected bool DropAreaGUI(Rect dropArea)
        {
            // Drag events reach a disabled (locked) inspector too.
            if (!GUI.enabled) return false;
            var evt = Event.current;
            int pickedCount = 0;
            bool recipesMerged = false;
            if (evt.type == EventType.MouseUp)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    dropSlotPicker.Open(dropArea);
                    Event.current.Use();
                }
            }
            var pickedSlot = dropSlotPicker.TakeSelection();
            if (pickedSlot != null)
            {
                LastSlot = pickedSlot.slotName;
                RecordRecipeUndo("Add Slot");
                AddSlotDataAsset(pickedSlot);
                pickedCount++;
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
                    // The drop changes this working copy; the inspector writes it into the recipe after this.
                    RecordRecipeUndo("Drop Into Recipe");

                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
                    for (int i = 0; i < draggedObjects.Length; i++)
                    {
                        if (draggedObjects[i])
                        {
                            SlotDataAsset tempSlotDataAsset = draggedObjects[i] as SlotDataAsset;
                            if (tempSlotDataAsset)
                            {
                                LastSlot = tempSlotDataAsset.slotName;
                                DraggedSlots.Add(tempSlotDataAsset);
                                continue;
                            }
                            if (draggedObjects[i] is OverlayDataAsset)
                            {
                                DraggedOverlays.Add(draggedObjects[i] as OverlayDataAsset);
                            }
                            if (draggedObjects[i] is UMATextRecipe)
                            {
                                var textRecipe = draggedObjects[i] as UMATextRecipe;
                                var recipe = textRecipe.GetCachedRecipe();
                                // A recipe that did not load completely would merge only part of itself.
                                if (recipe != null && recipe.missingAssets != null && recipe.missingAssets.Count > 0 &&
                                    !EditorUtility.DisplayDialog("Recipe not fully loaded",
                                        textRecipe.name + " uses assets that could not be found: " + string.Join(", ", recipe.missingAssets) +
                                        ".\n\nMerging adds only the parts that loaded.", "Merge what loaded", "Cancel"))
                                {
                                    recipe = null;
                                }
                                if (recipe != null)
                                {
                                    _recipe.Merge(recipe, false);
                                    recipesMerged = true;
                                }
                            }

                            var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
                            if (System.IO.Directory.Exists(path))
                            {
                                RecursiveScanFoldersForAssets(path);
                            }
                        }
                    }
                    if (DraggedSlots.Count > 0 || DraggedOverlays.Count > 0 || recipesMerged == true)
                    {
                        // A drop whose assets were all refused changes nothing, so nothing is saved.
                        bool added = AddDraggedFiles();
                        return added || recipesMerged;
                    }
                }
            }
            if (pickedCount > 0)
            {
                return true;
            }

            return false;
        }

        protected SlotDataAsset DrawIndexedSlotPicker(string label) => slotPicker.Draw(label);

        protected void AddSlotDataAsset(SlotDataAsset added)
        {
            var slot = new SlotData(added);
            _recipe.MergeSlot(slot, false);
        }

        /// <summary>
        /// Removes the slot a slot editor shows. Finds the slot itself: Remove Nulls and Clear Recipe
        /// change slot positions without rebuilding the editors, so the position the editor was built
        /// with can belong to a different slot by now.
        /// </summary>
        protected void RemoveEditedSlot(SlotEditor editor)
        {
            SlotData[] slots = _recipe != null ? _recipe.slotDataList : null;
            if (editor == null || editor.Slot == null || slots == null)
            {
                return;
            }
            int index = System.Array.IndexOf(slots, editor.Slot);
            if (index >= 0)
            {
                _recipe.SetSlot(index, null);
            }
        }

        /// <summary>Records Undo on the recipe asset before a change to the working copy that the inspector then writes into it.</summary>
        protected void RecordRecipeUndo(string undoName)
        {
            if (_recipeContext != null)
            {
                Undo.RecordObject(_recipeContext, undoName);
            }
        }

        private void SaveRecipeContext(string undoName = "Edit Recipe")
        {
            // Checks the incomplete-load lock and whether the recipe changed elsewhere since the open
            // inspector or graph loaded it; with Automatic Updates off the edit stays pending there.
            CharacterBaseEditor.WriteWorkingCopy(_recipeContext, _recipe, undoName);
        }

        protected void RecursiveScanFoldersForAssets(string path)
        {
            var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
            foreach (var assetFile in assetFiles)
            {
                var tempSlotDataAsset = AssetDatabase.LoadAssetAtPath(assetFile, typeof(SlotDataAsset)) as SlotDataAsset;
                if (tempSlotDataAsset)
                {
                    DraggedSlots.Add(tempSlotDataAsset);
                }
                var tempOverlayDataAsset = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(assetFile);
                if (tempOverlayDataAsset)
                {
                    DraggedOverlays.Add(tempOverlayDataAsset as OverlayDataAsset);
                }
            }
            foreach (var subFolder in System.IO.Directory.GetDirectories(path))
            {
                RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
            }
        }

        protected bool RaceInIndex(RaceData _raceData)
        {
            return UMAAssetIndexer.Instance.HasAsset<RaceData>(_raceData.raceName);
        }

        private static bool IsBakedRace(RaceData raceData)
        {
            return raceData != null && raceData.PrebakedBlendshapes != null && raceData.PrebakedBlendshapes.Count > 0;
        }

        private static bool IsBakedSlotName(string slotName, RaceData raceData)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                return false;
            }

            if (raceData != null && !string.IsNullOrEmpty(raceData.raceName))
            {
                return slotName.EndsWith("_baked_" + raceData.raceName, System.StringComparison.OrdinalIgnoreCase);
            }

            return slotName.IndexOf("_baked_", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetBakedSlotAssetName(SlotDataAsset slotAsset, RaceData raceData)
        {
            if (slotAsset == null || raceData == null)
            {
                return string.Empty;
            }

            string baseName = slotAsset.slotName;
            if (string.IsNullOrEmpty(baseName))
            {
                return string.Empty;
            }

            return baseName + "_baked_" + raceData.raceName;
        }

        private static List<string> GetBakedSlotNameCandidates(SlotDataAsset slotAsset, RaceData raceData)
        {
            List<string> candidates = new List<string>();
            if (slotAsset == null || raceData == null)
            {
                return candidates;
            }

            if (IsBakedSlotName(slotAsset.slotName, raceData))
            {
                candidates.Add(slotAsset.slotName);
            }

            if (!string.IsNullOrEmpty(slotAsset.slotName))
            {
                string slotNameCandidate = slotAsset.slotName + "_baked_" + raceData.raceName;
                if (!candidates.Contains(slotNameCandidate))
                {
                    candidates.Add(slotNameCandidate);
                }
            }

            // Older versions baked slots under Object.name rather than the logical slot name.
            // Keep those existing assets resolvable, but create new baked slots with slotName.
            string assetNameCandidate = string.IsNullOrEmpty(slotAsset.name)
                ? string.Empty : slotAsset.name + "_baked_" + raceData.raceName;
            if (!string.IsNullOrEmpty(assetNameCandidate) && !candidates.Contains(assetNameCandidate))
            {
                candidates.Add(assetNameCandidate);
            }

            return candidates;
        }

        internal static bool SlotHasBakeTargetsForRace(SlotDataAsset slotAsset, RaceData raceData)
        {
            if (slotAsset == null || raceData == null || raceData.PrebakedBlendshapes == null || raceData.PrebakedBlendshapes.Count == 0)
            {
                return false;
            }

            if (UMAMeshData.IsNullOrEmptyMeshData(slotAsset.meshData) || slotAsset.meshData.blendShapes == null || slotAsset.meshData.blendShapes.Length == 0)
            {
                return false;
            }

            for (int blendShapeIndex = 0; blendShapeIndex < slotAsset.meshData.blendShapes.Length; blendShapeIndex++)
            {
                UMABlendShape blendShape = slotAsset.meshData.blendShapes[blendShapeIndex];
                if (blendShape == null || string.IsNullOrEmpty(blendShape.shapeName))
                {
                    continue;
                }

                for (int optionIndex = 0; optionIndex < raceData.PrebakedBlendshapes.Count; optionIndex++)
                {
                    SlotBurnOptions bakeOption = raceData.PrebakedBlendshapes[optionIndex];
                    if (bakeOption == null || string.IsNullOrEmpty(bakeOption.BlendShape))
                    {
                        continue;
                    }

                    if (bakeOption.BlendShape == blendShape.shapeName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static SlotDataAsset ResolveBaseSlotAssetForRace(SlotData slot, RaceData raceData, out string displayName)
        {
            displayName = slot != null ? slot.slotName : string.Empty;
            if (slot == null || slot.asset == null)
            {
                return null;
            }

            // Baked races can still use ordinary eyes, lashes, mouth and utility slots.
            // Only resolve a baked counterpart when this slot contains a requested shape.
            if (IsBakedSlotName(slot.asset.slotName, raceData) ||
                !SlotHasBakeTargetsForRace(slot.asset, raceData))
            {
                return slot.asset;
            }

            List<string> bakedSlotNameCandidates = GetBakedSlotNameCandidates(slot.asset, raceData);
            if (bakedSlotNameCandidates.Count > 0)
            {
                var indexer = UMAAssetIndexer.Instance;
                if (indexer != null)
                {
                    for (int i = 0; i < bakedSlotNameCandidates.Count; i++)
                    {
                        string candidate = bakedSlotNameCandidates[i];
                        if (string.IsNullOrEmpty(candidate))
                        {
                            continue;
                        }

                        var bakedSlotAsset = indexer.GetAssetItem<SlotDataAsset>(candidate)?.Item as SlotDataAsset;
                        if (bakedSlotAsset != null)
                        {
                            displayName = bakedSlotAsset.slotName;
                            return bakedSlotAsset;
                        }
                    }
                }

                var bakedOnDemand = BakeBaseSlotAssetForRace(slot.asset, raceData, GetBakedSlotAssetName(slot.asset, raceData));
                if (bakedOnDemand != null)
                {
                    displayName = bakedOnDemand.slotName;
                    return bakedOnDemand;
                }
            }

            return slot.asset;
        }

        private static SlotDataAsset BakeBaseSlotAssetForRace(SlotDataAsset originalSlotAsset, RaceData raceData, string bakedSlotAssetName)
        {
            if (originalSlotAsset == null || raceData == null)
            {
                return null;
            }

            if (!IsBakedRace(raceData))
            {
                return null;
            }

            SlotDataAsset.BakeSlotParams options = new SlotDataAsset.BakeSlotParams();
            options.burnOptions = raceData.PrebakedBlendshapes;
            options.ShapesToInclude = raceData.UnbakedShapesToInclude;
            options.addToIndexer = true;
            options.smoothingAngleDegrees = -1.0f;
            options.forceRebuildRaceSlots = raceData.forceRebuildRaceSlots;
            options.newSlotName = bakedSlotAssetName;

            if (raceData.UnbakedShapesToInclude != null && raceData.UnbakedShapesToInclude.Count > 0)
            {
                options.copyUnbakedBlendshapes = true;
            }

            return originalSlotAsset.BakeNewSlotData(options);
        }

        public SlotMasterEditor(UMAData.UMARecipe recipe, UnityEngine.Object recipeContext = null)
        {
            _recipe = recipe;
            _recipeContext = recipeContext;

            if (recipe.slotDataList == null)
            {
                recipe.slotDataList = new SlotData[0];
            }
            for (int i = 0; i < recipe.slotDataList.Length; i++)
            {
                var slot = recipe.slotDataList[i];

                if (slot == null)
                {
                    continue;
                }

                _slotEditors.Add(new SlotEditor(_recipe, slot, i, _recipeContext));
            }

            if (_slotEditors.Count > 1)
            {
                List<SlotEditor> sortedSlots = new List<SlotEditor>(_slotEditors);
                sortedSlots.Sort(SlotEditor.comparer);

                for (int i = 1; i < sortedSlots.Count; i++)
                {
                    List<OverlayData> CurrentOverlays = sortedSlots[i].GetOverlays();
                    List<OverlayData> PreviousOverlays = sortedSlots[i - 1].GetOverlays();

                    if (CurrentOverlays == PreviousOverlays)
                    {
                        sortedSlots[i].sharedOverlays = true;
                    }
                }
            }
        }

        public virtual bool OnGUI(string targetName, ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            bool changed = false;

            RaceData newRace = (RaceData)EditorGUILayout.ObjectField("RaceData", _recipe.raceData, typeof(RaceData), false);
            if (_recipe.raceData == null)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.55f, 0.25f, 0.25f));
                GUILayout.Label("Warning: No race data is set!");
                GUIHelper.EndVerticalPadded(10);
            }

            if (_recipe.raceData != newRace)
            {
                _recipe.SetRace(newRace);
                _recipe.ClearDNAConverters();
                changed = true;
            }

            if (_recipe.raceData != null && !RaceInIndex(_recipe.raceData))
            {
                EditorGUILayout.HelpBox("Race " + _recipe.raceData.raceName + " is not indexed! Either assign it to an assetBundle or use one of the buttons below to add it to the Scene/Global Library.", MessageType.Error);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Add to Global Index (Recommended)"))
                {
                    // Saves the library alone, not the project's other unsaved assets.
                    UMAGlobalLibraryAutoRegistration.AddAndSave(_recipe.raceData);
                }
                GUILayout.EndHorizontal();
            }

            if (_sharedColorsEditor.OnGUI(_recipe))
            {
                changed = true;
                _textureDirty = true;
            }

            GUILayout.Space(10);
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag Slots, Overlays and recipes here. Click to Pick");
            if (DropAreaGUI(dropArea))
            {
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }
            GUILayout.Space(10);

            var baseSlotsList = new List<SlotDataAsset>();
            var baseSlotsNamesList = new List<string>() { "None" };
            if (_recipe.raceData != null)
            {
                if (_recipe.raceData.baseRaceRecipe != null)
                {
                    if (_recipe.raceData.baseRaceRecipe.name != targetName)
                    {
                        UMAData.UMARecipe thisBaseRecipe = _recipe.raceData.baseRaceRecipe.GetCachedRecipe();
                        SlotData[] thisBaseSlots = thisBaseRecipe.GetAllSlots();
                        foreach (SlotData slot in thisBaseSlots)
                        {
                            if (slot != null)
                            {
                                string slotName;
                                SlotDataAsset slotAsset = ResolveBaseSlotAssetForRace(slot, _recipe.raceData, out slotName);
                                if (slotAsset != null)
                                {
                                    baseSlotsList.Add(slotAsset);
                                    baseSlotsNamesList.Add(slotName);
                                }
                            }
                        }
                    }
                }
                if (baseSlotsNamesList.Count > 1)
                {
                    EditorGUI.BeginChangeCheck();
                    var baseAdded = EditorGUILayout.Popup("Add base Slot", 0, baseSlotsNamesList.ToArray());
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (baseAdded != 0)
                        {
                            var slotAsset = baseSlotsList[baseAdded - 1];
                            LastSlot = slotAsset.slotName;
                            var slotToAdd = new SlotData(slotAsset);
                            _recipe.MergeSlot(slotToAdd, false);
                            changed |= true;
                            _dnaDirty |= true;
                            _textureDirty |= true;
                            _meshDirty |= true;
                        }
                    }
                }
            }

            GUILayout.BeginHorizontal();
            var added = DrawIndexedSlotPicker("Add Slot");

            if (added != null)
            {
                LastSlot = added.slotName;
                var slot = new SlotData(added);
                RecordRecipeUndo("Add Slot");
                _recipe.MergeSlot(slot, false);
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }

            if (GUILayout.Button("Add Placeholder Slot", GUILayout.Width(160)))
            {
                // A new name each time: placeholders with the same name are one slot.
                string placeholderName = RecipeEditor.UniquePlaceholderName(_recipe, "NewPlaceholderSlot");
                var placeholder = SlotData.CreatePlaceholder(placeholderName, new string[0]);
                RecordRecipeUndo("Add Placeholder Slot");
                _recipe.MergeSlot(placeholder, false);
                LastSlot = placeholderName;
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(20);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear"))
            {
                // Undo restores the saved recipe; the inspector reloads it from there.
                if (_recipeContext != null)
                {
                    Undo.RecordObject(_recipeContext, "Clear Recipe");
                }
                _recipe.slotDataList = new SlotData[0];
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }
            if (GUILayout.Button("Remove Nulls"))
            {
                if (_recipeContext != null)
                {
                    Undo.RecordObject(_recipeContext, "Remove Empty Slots");
                }
                var newList = new List<SlotData>(_recipe.slotDataList.Length);
                foreach (var slotData in _recipe.slotDataList)
                {
                    if (slotData != null)
                    {
                        newList.Add(slotData);
                    }
                }
                _recipe.slotDataList = newList.ToArray();
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Collapse All"))
            {
                CollapseAll();
            }
            if (GUILayout.Button("Expand All"))
            {
                ExpandAll();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All Slots"))
            {
                SelectAllSlots();
            }
            if (GUILayout.Button("Select All Overlays"))
            {
                SelectAllOverlays();
            }
            GUILayout.EndHorizontal();

            if (LastSlot != "")
            {
                if (OpenSlots.ContainsKey(LastSlot))
                {
                    CollapseAll();
                    OpenSlots[LastSlot] = true;
                    LastSlot = "";
                }
            }

            var recipeSlots = _recipe.GetAllSlots();
            for (int i = 0; i < _slotEditors.Count; i++)
            {
                if (_slotEditors[i].Slot == null)
                {
                    continue;
                }

                bool found = false;
                for (int ri = 0; ri < recipeSlots.Length; ri++)
                {
                    if (recipeSlots[ri] == null)
                    {
                        continue;
                    }

                    if (_slotEditors[i].Slot.slotName == recipeSlots[ri].slotName)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Debug.Log("Recipe slots did not match slotEditor slots. Updating...");
                    return true;
                }
            }

            for (int i = 0; i < _slotEditors.Count; i++)
            {
                var editor = _slotEditors[i];

                if (editor == null)
                {
                    GUILayout.Label("Empty Slot");
                    continue;
                }

                if (_slotEditors[i].Slot.isBlendShapeSource)
                {
                    continue;
                }

                changed |= editor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);

                if (editor.Delete)
                {
                    _dnaDirty = true;
                    _textureDirty = true;
                    _meshDirty = true;

                    if (_recipeContext != null)
                    {
                        Undo.RecordObject(_recipeContext, "Remove Slot");
                    }
                    _slotEditors.RemoveAt(i);
                    RemoveEditedSlot(editor);
                    SaveRecipeContext();
                    i--;
                    changed = true;
                }
            }

            return changed;
        }

        private static void ExpandAll()
        {
            List<string> keys = new List<string>(OpenSlots.Keys);
            foreach (string s in keys)
            {
                OpenSlots[s] = true;
            }
        }

        private static void CollapseAll()
        {
            List<string> keys = new List<string>(OpenSlots.Keys);
            foreach (string s in keys)

            {
                OpenSlots[s] = false;
            }
        }

        protected void SelectAllSlots()
        {
            List<Object> slots = new List<Object>();
            foreach (var slotData in _recipe.slotDataList)
            {
                if (slotData != null && slotData.asset != null)
                {
                    slots.Add(slotData.asset);
                }
            }
            Selection.objects = slots.ToArray();
        }

        protected void SelectAllOverlays()
        {
            HashSet<Object> overlays = new HashSet<Object>();
            foreach (var slotData in _recipe.slotDataList)
            {
                if (slotData != null)
                {
                    List<OverlayData> overlayData = slotData.GetOverlayList();
                    foreach (var overlay in overlayData)
                    {
                        if (overlay != null)
                        {
                            overlays.Add(overlay.asset);
                        }
                    }
                }
            }
            Object[] newSelection = new Object[overlays.Count];
            overlays.CopyTo(newSelection);
            Selection.objects = newSelection;
        }
    }
}
#endif
