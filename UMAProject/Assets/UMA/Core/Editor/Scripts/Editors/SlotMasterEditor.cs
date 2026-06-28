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

        protected readonly UMAData.UMARecipe _recipe;
        protected readonly UnityEngine.Object _recipeContext;
        protected readonly List<SlotEditor> _slotEditors = new List<SlotEditor>();
        protected readonly SharedColorsCollectionEditor _sharedColorsEditor = new SharedColorsCollectionEditor();
        protected static int _slotPickerID = -1;

        protected List<SlotDataAsset> DraggedSlots = new List<SlotDataAsset>();
        protected List<OverlayDataAsset> DraggedOverlays = new List<OverlayDataAsset>();

        protected void AddDraggedFiles()
        {
            SlotData FirstSlot = null;

            if (DraggedSlots.Count >= 1 && DraggedOverlays.Count == 1)
            {
                foreach (SlotDataAsset sd in DraggedSlots)
                {
                    SlotData slot = new SlotData(sd);
                    slot.AddOverlay(new OverlayData(DraggedOverlays[0]));
                    slot = _recipe.MergeSlot(slot, false); 
                }
                return;
            }

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
        }

        protected bool DropAreaGUI(Rect dropArea)
        {
            var evt = Event.current;
            int pickedCount = 0;
            bool recipesMerged = false;
            if (evt.type == EventType.MouseUp)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    _slotPickerID = EditorGUIUtility.GetControlID(new GUIContent("slotObjectPicker"), FocusType.Passive);
                    EditorGUIUtility.ShowObjectPicker<SlotDataAsset>(null, false, "", _slotPickerID);
                    Event.current.Use();
                }
            }
            if (evt.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == _slotPickerID)
            {
                SlotDataAsset tempSlotDataAsset = EditorGUIUtility.GetObjectPickerObject() as SlotDataAsset;
                if (tempSlotDataAsset)
                {
                    LastSlot = tempSlotDataAsset.slotName;
                    AddSlotDataAsset(tempSlotDataAsset);
                    pickedCount++;
                    Event.current.Use();
                }
                else
                {
                    Event.current.Use();
                }
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
                        AddDraggedFiles();
                        return true;
                    }
                }
            }
            if (pickedCount > 0)
            {
                return true;
            }

            return false;
        }

        protected void AddSlotDataAsset(SlotDataAsset added)
        {
            var slot = new SlotData(added);
            _recipe.MergeSlot(slot, false);
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

            string baseName = !string.IsNullOrEmpty(slotAsset.name) ? slotAsset.name : slotAsset.slotName;
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

            string assetNameCandidate = GetBakedSlotAssetName(slotAsset, raceData);
            if (!string.IsNullOrEmpty(assetNameCandidate) && !candidates.Contains(assetNameCandidate))
            {
                candidates.Add(assetNameCandidate);
            }

            return candidates;
        }

        private static SlotDataAsset ResolveBaseSlotAssetForRace(SlotData slot, RaceData raceData, out string displayName)
        {
            displayName = slot != null ? slot.slotName : string.Empty;
            if (slot == null || slot.asset == null)
            {
                return null;
            }

            if (!IsBakedRace(raceData))
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

                        var bakedSlotAsset = indexer.GetAsset<SlotDataAsset>(candidate, false, true);
                        if (bakedSlotAsset != null)
                        {
                            displayName = bakedSlotAsset.slotName;
                            return bakedSlotAsset;
                        }
                    }
                }

                displayName = bakedSlotNameCandidates[0];

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
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(RaceData), _recipe.raceData);
                    UMAAssetIndexer.Instance.ForceSave();
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
            var added = (SlotDataAsset)EditorGUILayout.ObjectField("Add Slot", null, typeof(SlotDataAsset), false);

            if (added != null)
            {
                LastSlot = added.slotName;
                var slot = new SlotData(added);
                _recipe.MergeSlot(slot, false);
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }

            if (GUILayout.Button("Add Placeholder Slot", GUILayout.Width(160)))
            {
                string placeholderName = "NewPlaceholderSlot";
                var placeholder = SlotData.CreatePlaceholder(placeholderName, new string[0]);
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
                _recipe.slotDataList = new SlotData[0];
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }
            if (GUILayout.Button("Remove Nulls"))
            {
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

                    _slotEditors.RemoveAt(i);
                    _recipe.SetSlot(editor.idx, null);
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
