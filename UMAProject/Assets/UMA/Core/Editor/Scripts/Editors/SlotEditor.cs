#if UNITY_EDITOR
using System.Collections.Generic;
using UMA.Controls;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class SlotEditor
    {
        public List<SlotData> BlendShapeSlots = new List<SlotData>();
        public static Dictionary<string, string> TemporarySlotTags = new Dictionary<string, string>();
        public static Dictionary<string, int> SelectedRace = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInitializeOnLoad()
        {
            TemporarySlotTags = new Dictionary<string, string>();
            SelectedRace = new Dictionary<string, int>();
            _foldout = new Dictionary<string, bool>();
            _utilitiesFoldout = new Dictionary<string, bool>();
            _recentOverlayAssets.Clear();
            _recentOverlayNames.Clear();
            _recentOverlaysLoaded = false;
        }

        private readonly UMAData.UMARecipe _recipe;
        private readonly SlotData _slotData;
        private readonly UnityEngine.Object _recipeContext;
        private readonly List<OverlayData> _overlayData = new List<OverlayData>();
        private readonly List<OverlayEditor> _overlayEditors = new List<OverlayEditor>();
        private string _name;
        public UnityEditorInternal.ReorderableList SlotTagsList = null;
        private List<string> backingTags = new List<string>();
        private static Dictionary<string, bool> _foldout = new Dictionary<string, bool>();
        private static Dictionary<string, bool> _utilitiesFoldout = new Dictionary<string, bool>();
        private const string RecentOverlayEditorPrefsKeyPrefix = "UMA.SlotEditor.RecentOverlayName";
        private const int MaxRecentOverlays = 2;
        private static readonly List<OverlayDataAsset> _recentOverlayAssets = new List<OverlayDataAsset>(MaxRecentOverlays);
        private static readonly List<string> _recentOverlayNames = new List<string>(MaxRecentOverlays);
        private static bool _recentOverlaysLoaded;

        public SlotData Slot { get { return _slotData; } }

        public bool Delete { get; private set; }

        public bool FoldOut
        {
            get
            {
                if (!SlotMasterEditor.OpenSlots.ContainsKey(_slotData.slotName))
                {
                    SlotMasterEditor.OpenSlots.Add(_slotData.slotName, true);
                }

                return SlotMasterEditor.OpenSlots[_slotData.slotName];
            }
            set
            {
                if (!SlotMasterEditor.OpenSlots.ContainsKey(_slotData.slotName))
                {
                    SlotMasterEditor.OpenSlots.Add(_slotData.slotName, true);
                }

                SlotMasterEditor.OpenSlots[_slotData.slotName] = value;
            }
        }

        public bool sharedOverlays = false;
        public int idx;



        public SlotEditor(UMAData.UMARecipe recipe, SlotData slotData, int index, UnityEngine.Object recipeContext = null)
        {
            _recipe = recipe;
            _slotData = slotData;
            _recipeContext = recipeContext;
            _overlayData = slotData.GetOverlayList();

            this.idx = index;
            _name = slotData.isPlaceholderSlot ? slotData.placeholderSlotName : slotData.asset.slotName;
            for (int i = 0; i < _overlayData.Count; i++)
            {
                _overlayEditors.Add(new OverlayEditor(_recipe, slotData, _overlayData[i], null, _recipeContext));
            }
        }

        public List<OverlayData> GetOverlays()
        {
            return _overlayData;
        }

        private static string GetOverlayDisplayName(OverlayDataAsset overlay)
        {
            if (overlay == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(overlay.overlayName) ? overlay.name : overlay.overlayName;
        }

        private static string GetRecentOverlayPrefsKey(int index)
        {
            return RecentOverlayEditorPrefsKeyPrefix + index;
        }

        private static void SaveRecentOverlays()
        {
            for (int i = 0; i < MaxRecentOverlays; i++)
            {
                string prefKey = GetRecentOverlayPrefsKey(i);
                if (i < _recentOverlayNames.Count && !string.IsNullOrEmpty(_recentOverlayNames[i]))
                {
                    EditorPrefs.SetString(prefKey, _recentOverlayNames[i]);
                }
                else
                {
                    EditorPrefs.DeleteKey(prefKey);
                }
            }
        }

        private static void RememberRecentOverlay(OverlayDataAsset overlay)
        {
            if (overlay == null)
            {
                return;
            }

            string overlayName = GetOverlayDisplayName(overlay);
            if (string.IsNullOrEmpty(overlayName))
            {
                return;
            }

            _recentOverlaysLoaded = true;

            for (int i = _recentOverlayNames.Count - 1; i >= 0; i--)
            {
                if (_recentOverlayNames[i] == overlayName)
                {
                    _recentOverlayNames.RemoveAt(i);
                    _recentOverlayAssets.RemoveAt(i);
                }
            }

            _recentOverlayNames.Insert(0, overlayName);
            _recentOverlayAssets.Insert(0, overlay);

            while (_recentOverlayNames.Count > MaxRecentOverlays)
            {
                _recentOverlayNames.RemoveAt(_recentOverlayNames.Count - 1);
                _recentOverlayAssets.RemoveAt(_recentOverlayAssets.Count - 1);
            }

            SaveRecentOverlays();
        }

        private static List<OverlayDataAsset> GetRecentOverlays()
        {
            if (!_recentOverlaysLoaded)
            {
                _recentOverlayNames.Clear();
                _recentOverlayAssets.Clear();

                for (int i = 0; i < MaxRecentOverlays; i++)
                {
                    string overlayName = EditorPrefs.GetString(GetRecentOverlayPrefsKey(i), string.Empty);
                    if (string.IsNullOrEmpty(overlayName))
                    {
                        continue;
                    }

                    OverlayDataAsset overlay = UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>(overlayName);
                    if (overlay == null)
                    {
                        continue;
                    }

                    _recentOverlayNames.Add(overlayName);
                    _recentOverlayAssets.Add(overlay);
                }

                _recentOverlaysLoaded = true;
            }

            return _recentOverlayAssets;
        }

        private void AddOverlayToSlot(OverlayDataAsset overlay)
        {
            var newOverlay = new OverlayData(overlay);
            _overlayEditors.Add(new OverlayEditor(_recipe, _slotData, newOverlay, null, _recipeContext));
            _overlayData.Add(newOverlay);
            RememberRecentOverlay(overlay);
            SaveRecipeContext();
        }

        private void SaveRecipeContext()
        {
            if (_recipeContext == null)
            {
                return;
            }

            if (_recipeContext is UMARecipeBase recipeBase)
            {
                recipeBase.Save(_recipe);
                EditorUtility.SetDirty(recipeBase);
                if (EditorUtility.IsPersistent(recipeBase))
                {
                    AssetDatabase.SaveAssetIfDirty(recipeBase);
                }

                if (recipeBase is UMATextRecipe textRecipe)
                {
                    UMAUpdateProcessor.UpdateRecipe(textRecipe);
                }
                return;
            }

            EditorUtility.SetDirty(_recipeContext);
            if (EditorUtility.IsPersistent(_recipeContext))
            {
                AssetDatabase.SaveAssetIfDirty(_recipeContext);
            }
        }

        private bool InIndex(SlotData _slotData)
        {
            if (_slotData.isPlaceholderSlot) return true;
            return UMAAssetIndexer.Instance.HasSlot(_slotData.asset.slotName);
        }

        public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            bool delete;
            bool select;
            bool selectInLibrary;
            bool _foldOut = FoldOut;
            bool missingPlaceholderTags = HasMissingPlaceholderTags();
            bool needsFixup = NeedsFixup();

            string barLabel = _slotData.isPlaceholderSlot
                ? _name + "      (Placeholder Wildcard)"
                : _name + "      (" + _slotData.asset.name + ")" + GetLodSuffix(_slotData);

            // Draw foldout bar with Asset, Lib, and X buttons
            DrawSlotFoldoutBar(ref _foldOut, barLabel, out select, out selectInLibrary, out delete,
                missingPlaceholderTags || needsFixup ? GetRedFoldoutStyle() : EditorStyles.foldout);

            FoldOut = _foldOut;

            Delete = delete;

            if (selectInLibrary && !_slotData.isPlaceholderSlot)
            {
                SelectInLibrary(_slotData);
            }

            if (select && !_slotData.isPlaceholderSlot)
            {
                EditorGUIUtility.PingObject(_slotData.asset.GetUmaObjectId());
                InspectorUtlity.InspectTarget(_slotData.asset);
            }

            if (!FoldOut)
            {
                return false;
            }

            bool changed = false;

            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
            {
                if (!InIndex(_slotData))
                {
                    EditorGUILayout.HelpBox("Slot " + _name + " is not indexed!", MessageType.Error);

                    GUILayout.BeginHorizontal();

                    if (!_slotData.isPlaceholderSlot && GUILayout.Button("Add to Global Index (Recommended)"))
                    {
                        UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), _slotData.asset);
                        UMAAssetIndexer.Instance.ForceSave();
                    }
                    GUILayout.EndHorizontal();
                }

                // Placeholder slot specific GUI
                if (_slotData.isPlaceholderSlot)
                {
                    EditorGUILayout.HelpBox("This is a placeholder wildcard slot. It has no backing asset. Its overlays will be applied to matching tagged slots at build time.", MessageType.Info);

                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUILayout.DelayedTextField("Placeholder Name", _slotData.placeholderSlotName);
                    if (EditorGUI.EndChangeCheck() && newName != _slotData.placeholderSlotName)
                    {
                        _slotData.placeholderSlotName = newName;
                        _name = newName;
                        changed = true;
                    }
                }

                bool disabled = _slotData.isDisabled;
                _slotData.isDisabled = EditorGUILayout.Toggle("Disable in recipe:", _slotData.isDisabled);

                if (disabled != _slotData.isDisabled)
                {
                    changed = true;
                }

                // Utilities foldout
                if (!_slotData.isPlaceholderSlot)
                {
                    if (!_utilitiesFoldout.ContainsKey(_slotData.slotName))
                    {
                        _utilitiesFoldout.Add(_slotData.slotName, false);
                    }
                    GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
                    GUILayout.Space(10);
                    _utilitiesFoldout[_slotData.slotName] = EditorGUILayout.Foldout(_utilitiesFoldout[_slotData.slotName], "Utilities");
                    GUILayout.EndHorizontal();

                    if (_utilitiesFoldout[_slotData.slotName])
                    {
                        GUIHelper.BeginVerticalPadded(10, new Color(0.9f, 0.9f, 0.9f));
                        {
                            // View copied data (moved here)
                            _slotData.slotAssetFoldout = EditorGUILayout.Foldout(_slotData.slotAssetFoldout, "View copied data", true);
                            if (_slotData.slotAssetFoldout)
                            {
                                GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
                                {
                                    EditorGUILayout.LabelField("Overlay Scale", _slotData.overlayScale.ToString("F4"));
                                    EditorGUILayout.LabelField("Matching Tags");
                                    if (_slotData.tags != null && _slotData.tags.Length > 0)
                                    {
                                        foreach (var tag in _slotData.tags)
                                        {
                                            EditorGUILayout.LabelField(" - " + tag);
                                        }
                                    }
                                    else
                                    {
                                        EditorGUILayout.LabelField(" - None");
                                    }
                                    EditorGUILayout.LabelField("Matching Races");
                                    if (_slotData.Races != null && _slotData.Races.Length > 0)
                                    {
                                        foreach (var race in _slotData.Races)
                                        {
                                            EditorGUILayout.LabelField(" - " + race);
                                        }
                                    }
                                    else
                                    {
                                        EditorGUILayout.LabelField(" - None");
                                    }
                                    if (GUILayout.Button("Refresh slot from Asset"))
                                    {
                                        _slotData.asset = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(AssetDatabase.GetAssetPath(_slotData.asset));
                                        _slotData.UpdateFromAsset(_slotData.asset);
                                        changed = true;
                                        SaveRecipeContext();
                                        GUIUtility.ExitGUI();
                                    }
                                }
                                GUIHelper.EndVerticalPadded(10);
                            }

                            GUILayout.Space(4);
                            EditorGUILayout.LabelField("Update Overlay UMA Material", EditorStyles.boldLabel);
                            Rect matDrop = GUILayoutUtility.GetRect(0.0f, 40.0f, GUILayout.ExpandWidth(true));
                            GUI.Box(matDrop, "Drag UMA Material here to update this slot's overlays");
                            Event evt = Event.current;
                            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && matDrop.Contains(evt.mousePosition))
                            {
                                bool hasUmaMaterial = false;
                                foreach (var obj in DragAndDrop.objectReferences)
                                {
                                    if (obj is UMAMaterial)
                                    {
                                        hasUmaMaterial = true;
                                        break;
                                    }
                                }
                                if (hasUmaMaterial)
                                {
                                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                                    if (evt.type == EventType.DragPerform)
                                    {
                                        DragAndDrop.AcceptDrag();
                                        foreach (var obj in DragAndDrop.objectReferences)
                                        {
                                            if (obj is UMAMaterial umaMat)
                                            {
                                                if (ApplyUMAMaterialToSlotAndOverlays(umaMat))
                                                {
                                                    changed = true;
                                                    _textureDirty = true;
                                                    _meshDirty = true;
                                                }
                                            }
                                        }
                                        evt.Use();
                                    }
                                    else
                                    {
                                        evt.Use();
                                    }
                                }
                            }
                        }
                        GUIHelper.EndVerticalPadded(10);
                    }
                }

            if (!_slotData.isPlaceholderSlot && _slotData.asset.isClippingPlane)
            {
                EditorGUILayout.HelpBox("This slot is a clipping plane. It will not be rendered in the scene.", MessageType.Info);
                GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
                GUILayout.Space(10);
                _slotData.ClipPlaneFoldout = EditorGUILayout.Foldout(_slotData.ClipPlaneFoldout, "Clipping Parameters");
                GUILayout.EndHorizontal();
                if (_slotData.ClipPlaneFoldout)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.HelpBox("Smoosh Amount is the distance from the scalp to smoosh hair. Try 0.001 or 0.002\nSmoosh Buffer is the 'ease in' distance to smooth outside of the hat\n", MessageType.Info);
                    _slotData.smooshDistance = EditorGUILayout.FloatField("Smoosh Amount", _slotData.smooshDistance);
                    _slotData.overSmoosh = EditorGUILayout.FloatField("Smoosh Buffer", _slotData.overSmoosh);
                    _slotData.smooshInvertDist = EditorGUILayout.Toggle("Invert Smoosh Distance", _slotData.smooshInvertDist);
                    _slotData.smooshInvertX = EditorGUILayout.Toggle("Invert Smoosh X", _slotData.smooshInvertX);
                    _slotData.smooshInvertY = EditorGUILayout.Toggle("Invert Smoosh Y", _slotData.smooshInvertY);
                    _slotData.smooshInvertZ = EditorGUILayout.Toggle("Invert Smoosh Z", _slotData.smooshInvertZ);
                    EditorGUILayout.HelpBox("Override Target Tag and Smooshed Tag are used to override the default tags to find the target and smooshed slots. This is useful if you have multiple clipping planes and want to use different tags for each one. By default, the target is  'Smooshtarget' and the smooshed slot is 'Smooshable'", MessageType.Info);
                    _slotData.smooshTargetTag = EditorGUILayout.TextField("Override Target Tag", _slotData.smooshTargetTag);
                    _slotData.smooshableTag = EditorGUILayout.TextField("Override Smooshed Tag", _slotData.smooshableTag);

                    changed = EditorGUI.EndChangeCheck();

                }
            }
            else
            {
                if (!_slotData.isPlaceholderSlot)
                {
                GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
                GUILayout.Space(10);
                _slotData.BlendshapeFoldout = EditorGUILayout.Foldout(_slotData.BlendshapeFoldout, "Additional Blendshape Slots");
                GUILayout.EndHorizontal();

                if (_slotData.BlendshapeFoldout)
                {
                    BlendShapeSlots = new List<SlotData>();
                    foreach (SlotData sd in _recipe.slotDataList)
                    {
                        if (sd == null)
                        {
                            continue;
                        }

                        if (sd.isBlendShapeSource && sd.blendShapeTargetSlot == _slotData.slotName)
                        {
                            BlendShapeSlots.Add(sd);
                        }
                    }

                    GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
                    {
                        foreach (SlotData sda in BlendShapeSlots)
                        {
                            bool removeBlendshapeSlot = false;
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(sda.slotName, EditorStyles.textField, GUILayout.ExpandWidth(true));
                            if (GUILayout.Button("X", GUILayout.Width(22)))
                            {
                                removeBlendshapeSlot = true;
                            }
                            GUILayout.EndHorizontal();

                            if (removeBlendshapeSlot)
                            {
                                _recipe.RemoveSlot(sda);
                                _dnaDirty = true;
                                _meshDirty = true;
                                changed = true;
                                SaveRecipeContext();
                                GUIUtility.ExitGUI();
                            }
                        }
                        var addedSlot = (SlotDataAsset)EditorGUILayout.ObjectField("Add Slot", null, typeof(SlotDataAsset), false);

                        if (addedSlot != null)
                        {
                            bool OK = true;

                            if (addedSlot.meshData.vertexCount != _slotData.asset.meshData.vertexCount)
                            {
                                EditorUtility.DisplayDialog("Error", "Slot " + addedSlot.slotName + " Does not have the same vertex count as slot " + _slotData.asset.slotName, "OK");
                                OK = false;
                            }
                            if (OK && !HasBlendshapes(addedSlot))
                            {
                                EditorUtility.DisplayDialog("Error", "Slot " + addedSlot.slotName + " Does not have any blendshapes!", "OK");
                                OK = false;
                            }
                            if (OK)
                            {
                                foreach (SlotData sda in BlendShapeSlots)
                                {
                                    if (sda.slotName == addedSlot.slotName)
                                    {
                                        EditorUtility.DisplayDialog("Error", "Slot " + sda.slotName + " already exists in list!", "OK");
                                        OK = false;
                                        break;
                                    }
                                }
                            }
                            if (OK)
                            {
                                if (_recipeContext != null)
                                {
                                    Undo.RecordObject(_recipeContext, "Add Additional Blendshape Slot");
                                }
                                var newSlot = new SlotData(addedSlot);
                                newSlot.blendShapeTargetSlot = _slotData.slotName;
                                newSlot.SetOverlayList(new List<OverlayData>());
                                _recipe.MergeSlot(newSlot, false);
                                _dnaDirty = true;
                                _textureDirty = true;
                                _meshDirty = true;
                                changed = true;
                                SaveRecipeContext();
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                    GUIHelper.EndVerticalPadded(10);
                }
                } // end !isPlaceholderSlot blendshape guard

                if (!TemporarySlotTags.ContainsKey(_slotData.slotName))
                {
                    TemporarySlotTags.Add(_slotData.slotName, "");
                }
                if (!_foldout.ContainsKey(_slotData.slotName))
                {
                    _foldout.Add(_slotData.slotName, false);
                }
                GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
                GUILayout.Space(10);
                _foldout[_slotData.slotName] = missingPlaceholderTags
                    ? EditorGUILayout.Foldout(_foldout[_slotData.slotName], "Matching Criteria", true, GetRedFoldoutStyle())
                    : EditorGUILayout.Foldout(_foldout[_slotData.slotName], "Matching Criteria");
                GUILayout.EndHorizontal();
                if (_foldout[_slotData.slotName])
                {
                    GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
                    {
                        if (_slotData.isPlaceholderSlot || (_slotData.asset != null && _slotData.asset.isWildCardSlot))
                        {
                            GUILayout.Label("Match Tags:");
                        }
                        else
                        {
                            GUILayout.Label("Edit tags for this slot:");
                        }
                        if (SlotTagsList == null)
                        {
                            backingTags = new List<string>(_slotData.tags);
                            SlotTagsList = GUIHelper.InitGenericTagsList(backingTags);
                        }
                        SlotTagsList.DoLayoutList();
                        if (GUI.changed)
                        {
                            _slotData.tags = backingTags.ToArray();
                            changed = true;
                        }
                        TagsEditor.DoRaceGUI(ref changed, _slotData);
                    }
                    GUIHelper.EndVerticalPadded(10);
                }

                if (!_slotData.isPlaceholderSlot)
                {
                    EditorGUILayout.HelpBox("Expand Along Normal is used to expand the slot along the normal of the mesh. This is useful for offsetting to address zfighting issues. In micrometers", MessageType.Info);
                    GUI.changed = false;
                    _slotData.expandAlongNormal = EditorGUILayout.DelayedIntField("Expand Along Normal", _slotData.expandAlongNormal);
                    if (GUI.changed)
                    {
                        changed = true;
                    }
                }
                if (sharedOverlays)
                {
                    List<OverlayData> ovr = GetOverlays();

                    EditorGUILayout.LabelField("Shared Overlays:");
                    GUIHelper.BeginVerticalPadded(10, new Color(0.85f, 0.85f, 0.85f));
                    {
                        foreach (OverlayData ov in ovr)
                        {
                            EditorGUILayout.LabelField(ov.asset.overlayName);
                        }
                    }
                    GUIHelper.EndVerticalPadded(10);
                }
                else
                {
                    var added = (OverlayDataAsset)EditorGUILayout.ObjectField("Add Overlay", null, typeof(OverlayDataAsset), false);

                    if (added != null)
                    {
                        AddOverlayToSlot(added);
                        _dnaDirty = true;
                        _textureDirty = true;
                        _meshDirty = true;
                        changed = true;
                        GUIUtility.ExitGUI();
                    }

                    List<OverlayDataAsset> recentOverlays = GetRecentOverlays();
                    for (int overlayIndex = 0; overlayIndex < MaxRecentOverlays; overlayIndex++)
                    {
                        OverlayDataAsset recentOverlay = overlayIndex < recentOverlays.Count ? recentOverlays[overlayIndex] : null;
                        string overlayLabel = recentOverlay != null ? GetOverlayDisplayName(recentOverlay) : "None";
                        string rowLabel = overlayIndex == 0 ? "Last Overlay: " : "Previous Overlay: ";
                        bool addRecentOverlay = false;

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(rowLabel + overlayLabel);
                        using (new EditorGUI.DisabledScope(recentOverlay == null))
                        {
                            if (GUILayout.Button("Add", GUILayout.Width(80)))
                            {
                                addRecentOverlay = true;
                            }
                        }
                        EditorGUILayout.EndHorizontal();

                        if (addRecentOverlay)
                        {
                            AddOverlayToSlot(recentOverlay);
                            _dnaDirty = true;
                            _textureDirty = true;
                            _meshDirty = true;
                            changed = true;
                            GUIUtility.ExitGUI();
                        }
                    }

                    if (!_slotData.isPlaceholderSlot)
                    {
                        var addedSlot = (SlotDataAsset)EditorGUILayout.ObjectField("Add Slot", null, typeof(SlotDataAsset), false);

                        if (addedSlot != null)
                        {
                            var newSlot = new SlotData(addedSlot);
                            newSlot.SetOverlayList(_slotData.GetOverlayList());
                            _recipe.MergeSlot(newSlot, false);
                            _dnaDirty = true;
                            _textureDirty = true;
                            _meshDirty = true;
                            changed = true;
                            SaveRecipeContext();
                            GUIUtility.ExitGUI();
                        }

                        int remapUV = EditorGUILayout.Popup("Remap UV to Main", _slotData.UVSet, new string[] { "None", "UV Set 2", "UV Set 3", "UV Set 4" });
                        if (remapUV != _slotData.UVSet)
                        {
                            _slotData.UVSet = remapUV;
                            _meshDirty = true;
                            changed = true;
                        }
                    }

                    for (int i = 0; i < _overlayEditors.Count; i++)
                    {
                        var overlayEditor = _overlayEditors[i];

                        if (overlayEditor.OnGUI())
                        {
                            _textureDirty = true;
                            changed = true;
                        }

                        if (overlayEditor.Delete)
                        {
                            _overlayEditors.RemoveAt(i);
                            _overlayData.RemoveAt(i);
                            _textureDirty = true;
                            changed = true;
                            SaveRecipeContext();
                            GUIUtility.ExitGUI();
                        }
                    }

                    for (int i = 0; i < _overlayEditors.Count; i++)
                    {
                        var overlayEditor = _overlayEditors[i];
                        if (overlayEditor.move > 0 && i + 1 < _overlayEditors.Count)
                        {
                            _overlayEditors[i] = _overlayEditors[i + 1];
                            _overlayEditors[i + 1] = overlayEditor;

                            var overlayData = _overlayData[i];
                            _overlayData[i] = _overlayData[i + 1];
                            _overlayData[i + 1] = overlayData;

                            overlayEditor.move = 0;
                            _textureDirty = true;
                            changed = true;
                            continue;
                        }

                        if (overlayEditor.move < 0 && i > 0)
                        {
                            _overlayEditors[i] = _overlayEditors[i - 1];
                            _overlayEditors[i - 1] = overlayEditor;

                            var overlayData = _overlayData[i];
                            _overlayData[i] = _overlayData[i - 1];
                            _overlayData[i - 1] = overlayData;

                            overlayEditor.move = 0;
                            _textureDirty = true;
                            changed = true;
                            continue;
                        }
                    }

                    if (!_slotData.isPlaceholderSlot)
                    {
                        GUILayout.Space(8);
                        if (GUILayout.Button("Convert to Placeholder"))
                        {
                            ConvertToPlaceholder();
                            _dnaDirty = true;
                            _textureDirty = true;
                            _meshDirty = true;
                            changed = true;
                            SaveRecipeContext();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                }
            }
            GUIHelper.EndVerticalPadded(10);

            return changed;
        }

        private bool HasMissingPlaceholderTags()
        {
            return _slotData.isPlaceholderSlot && (_slotData.tags == null || _slotData.tags.Length == 0);
        }

        private static string GetLodSuffix(SlotData slotData)
        {
            if (slotData == null || slotData.asset == null)
                return " (No LOD)";

            var md = slotData.asset.meshData;
            if (UMAMeshData.IsNullOrEmptyMeshData(md))
                return " (No LOD)";

            if (md.submeshes == null || md.submeshes.Length == 0 || md.submeshes[0] == null)
                return " (No LOD)";

            int lodCount = md.submeshes[0].LODCount();
            return lodCount > 0 ? $" ({lodCount} LODs)" : " (No LOD)";
        }

        private bool NeedsFixup()
        {
            if (_slotData == null || _slotData.isPlaceholderSlot || _slotData.asset == null)
                return false;

            var md = _slotData.asset.meshData;
            if (UMAMeshData.IsNullOrEmptyMeshData(md))
                return false;

            // Legacy UMA 2 slot that still has isLegacySlot flag
            if (_slotData.asset.isLegacySlot)
                return true;

            // Has legacy bone weights (not yet converted to managed format)
            if (md.boneWeights != null && md.boneWeights.Length > 0 && md.ManagedBoneWeights == null)
                return true;

            // Dual bone weight systems (both legacy and managed present)
            if (md.boneWeights != null && md.boneWeights.Length > 0 && md.ManagedBoneWeights != null && md.ManagedBoneWeights.Length > 0)
                return true;

            // Missing vertex colors
            int vertCount = md.vertices != null ? md.vertices.Length : md.vertexCount;
            if (vertCount > 0 && (md.colors32 == null || md.colors32.Length != vertCount))
                return true;

            // vertexCount mismatch
            if (md.vertexCount != vertCount)
                return true;

            return false;
        }

        private static GUIStyle GetRedFoldoutStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.foldout);
            /*
            style.normal.textColor = Color.red;
            style.onNormal.textColor = Color.red;
            style.hover.textColor = Color.red;
            style.onHover.textColor = Color.red;
            style.focused.textColor = Color.red;
            style.onFocused.textColor = Color.red;
            style.active.textColor = Color.red;
            style.onActive.textColor = Color.red;*/
            return style;
        }

        private static void DrawSlotFoldoutBar(ref bool foldout, string content, out bool assetPressed, out bool libPressed, out bool delete, GUIStyle foldoutStyle)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            foldout = EditorGUILayout.Foldout(foldout, content, true, foldoutStyle);
            assetPressed = GUILayout.Button("Asset", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            libPressed = GUILayout.Button("Lib", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        private void SelectInLibrary(SlotData slotData)
        {
            if (slotData == null || slotData.asset == null)
                return;

            // Ping the asset
            EditorGUIUtility.PingObject(slotData.asset);

            // Open the Global Library window docked next to the Scene view
            var sceneView = EditorWindow.GetWindow<SceneView>();
            var libraryWindow = sceneView != null
                ? EditorWindow.GetWindow<AssetIndexerWindow>("Global Library", typeof(SceneView))
                : AssetIndexerWindow.GetWindow();

            libraryWindow.Focus();

            // Delay the selection to let the window initialize its tree view
            EditorApplication.delayCall += () =>
            {
                if (libraryWindow == null || libraryWindow.treeView == null || libraryWindow.treeView.treeModel == null)
                    return;

                var treeElements = new List<AssetTreeElement>();
                TreeElementUtility.TreeToList(libraryWindow.treeView.treeModel.root, treeElements);

                string slotName = slotData.slotName;
                foreach (var element in treeElements)
                {
                    if (element.ai != null && element.ai._Name == slotName)
                    {
                        libraryWindow.treeView.SetSelection(new List<int> { element.id });
                        libraryWindow.treeView.FrameItem(element.id);
                        libraryWindow.Repaint();
                        return;
                    }
                }
            };
        }

        private bool ApplyUMAMaterialToSlotAndOverlays(UMAMaterial umaMat)
        {
            if (umaMat == null || _slotData == null || _slotData.asset == null)
                return false;

            bool changed = false;
            int channelCount = (umaMat.channels != null) ? umaMat.channels.Length : 0;

            // Update overlays on this slot
            if (_overlayData != null)
            {
                foreach (var od in _overlayData)
                {
                    if (od == null || od.asset == null)
                        continue;

                    var oda = od.asset;
                    Undo.RecordObject(oda, "Update Overlay UMA Material");
                    oda.material = umaMat;

                    // Ensure overlay asset texture list matches channel count
                    try
                    {
                        var texList = oda.textureList; // assuming public property exists
                        if (texList == null || texList.Length != channelCount)
                        {
                            var newList = new Texture[channelCount];
                            if (texList != null)
                            {
                                System.Array.Copy(texList, newList, Mathf.Min(texList.Length, newList.Length));
                            }
                            oda.textureList = newList;
                        }
                    }
                    catch { }

                    EditorUtility.SetDirty(oda);
                    AssetDatabase.SaveAssetIfDirty(oda);

                    // Ensure runtime overlay arrays match new material channel count
                    od.Validate();
                    changed = true;
                }
            }

            return changed;
        }

        private void ConvertToPlaceholder()
        {
            if (_slotData == null || _slotData.isPlaceholderSlot)
            {
                return;
            }

            var slotName = _slotData.slotName;
            _slotData.placeholderSlotName = slotName;
            _slotData.asset = null;
            _slotData.isPlaceholderSlot = true;
            _slotData.BlendshapeFoldout = false;
            _slotData.ClipPlaneFoldout = false;
            _slotData.slotAssetFoldout = false;
            _name = slotName;
        }

        public bool HasBlendshapes(SlotDataAsset sda)
        {
            if (sda.meshData.blendShapes == null)
            {
                return false;
            }

            if (sda.meshData.blendShapes.Length < 1)
            {
                return false;
            }

            return true;
        }

        public static NameSorter sorter = new NameSorter();
        public class NameSorter : IComparer<SlotEditor>
        {
            public int Compare(SlotEditor x, SlotEditor y)
            {
                return string.Compare(x._slotData.slotName, y._slotData.slotName);
            }
        }
        public static Comparer comparer = new Comparer();
        public class Comparer : IComparer<SlotEditor>
        {
            public int Compare(SlotEditor x, SlotEditor y)
            {
                if (x._overlayData == y._overlayData)
                {
                    return 0;
                }

                if (x._overlayData == null)
                {
                    return 1;
                }

                if (y._overlayData == null)
                {
                    return -1;
                }

                return x._overlayData.GetHashCode() - y._overlayData.GetHashCode();
            }
        }
    }
}
#endif
