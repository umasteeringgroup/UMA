#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class SlotEditor
    {
        public List<SlotData> BlendShapeSlots = new List<SlotData>();
        public static Dictionary<string, string> TemporarySlotTags = new Dictionary<string, string>();
        public static Dictionary<string, int> SelectedRace = new Dictionary<string, int>();

        private readonly UMAData.UMARecipe _recipe;
        private readonly SlotData _slotData;
        private readonly List<OverlayData> _overlayData = new List<OverlayData>();
        private readonly List<OverlayEditor> _overlayEditors = new List<OverlayEditor>();
        private readonly string _name;
        public UnityEditorInternal.ReorderableList SlotTagsList = null;
        private List<string> backingTags = new List<string>();
        private static Dictionary<string, bool> _foldout = new Dictionary<string, bool>();

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



        public SlotEditor(UMAData.UMARecipe recipe, SlotData slotData, int index)
        {
            _recipe = recipe;
            _slotData = slotData;
            _overlayData = slotData.GetOverlayList();

            this.idx = index;
            _name = slotData.asset.slotName;
            for (int i = 0; i < _overlayData.Count; i++)
            {
                _overlayEditors.Add(new OverlayEditor(_recipe, slotData, _overlayData[i]));
            }
        }

        public List<OverlayData> GetOverlays()
        {
            return _overlayData;
        }

        private bool InIndex(SlotData _slotData)
        {
            return UMAAssetIndexer.Instance.HasSlot(_slotData.asset.slotName);
        }

        public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            bool delete;
            bool select;
            bool _foldOut = FoldOut;

            GUIHelper.FoldoutBarButton(ref _foldOut, _name + "      (" + _slotData.asset.name + ")", "inspect", out select, out delete);

            FoldOut = _foldOut;

            Delete = delete;

            if (select)
            {
                EditorGUIUtility.PingObject(_slotData.asset.GetInstanceID());
                InspectorUtlity.InspectTarget(_slotData.asset);
            }

            if (!FoldOut)
            {
                return false;
            }

            bool changed = false;

            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

            if (!InIndex(_slotData))
            {
                EditorGUILayout.HelpBox("Slot " + _slotData.asset.name + " is not indexed!", MessageType.Error);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Add to Global Index (Recommended)"))
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), _slotData.asset);
                    UMAAssetIndexer.Instance.ForceSave();
                }
                GUILayout.EndHorizontal();
            }

            bool disabled = _slotData.isDisabled;
            _slotData.isDisabled = EditorGUILayout.Toggle("Disable in recipe:", _slotData.isDisabled);

            if (disabled != _slotData.isDisabled)
            {
                changed = true;
            }
            _slotData.slotAssetFoldout = EditorGUILayout.Foldout(_slotData.slotAssetFoldout, "View copied data", true);
            if (_slotData.slotAssetFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
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
                }
                GUIHelper.EndVerticalPadded(10);
            }

            if (_slotData.asset.isClippingPlane)
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
                    try
                    {
                        bool wasDeleted = false;
                        foreach (SlotData sda in BlendShapeSlots)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(sda.slotName, EditorStyles.textField, GUILayout.ExpandWidth(true));
                            if (GUILayout.Button("X", GUILayout.Width(22)))
                            {
                                _recipe.RemoveSlot(sda);
                                wasDeleted = true;
                            }
                            GUILayout.EndHorizontal();
                        }
                        if (wasDeleted)
                        {
                            _dnaDirty = true;
                            _meshDirty = true;
                            changed = true;
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
                                var newSlot = new SlotData(addedSlot);
                                newSlot.blendShapeTargetSlot = _slotData.slotName;
                                newSlot.SetOverlayList(new List<OverlayData>());
                                _recipe.MergeSlot(newSlot, false);
                                _dnaDirty = true;
                                _textureDirty = true;
                                _meshDirty = true;
                                changed = true;
                            }
                        }
                    }
                    finally
                    {
                        GUIHelper.EndVerticalPadded(10);
                    }
                }

                if (!TemporarySlotTags.ContainsKey(_slotData.slotName))
                {
                    TemporarySlotTags.Add(_slotData.slotName, "");
                }
                if (!_foldout.ContainsKey(_slotData.slotName))
                {
                    _foldout.Add(_slotData.slotName, false);
                }
                GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
                GUILayout.Space(10); _foldout[_slotData.slotName] = EditorGUILayout.Foldout(_foldout[_slotData.slotName], "Matching Criteria");
                GUILayout.EndHorizontal();
                if (_foldout[_slotData.slotName])
                {
                    GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
                    if (_slotData.asset.isWildCardSlot)
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
                    GUIHelper.EndVerticalPadded(10);
                }

                EditorGUILayout.HelpBox("Expand Along Normal is used to expand the slot along the normal of the mesh. This is useful for offsetting to address zfighting issues. In micrometers", MessageType.Info);
                GUI.changed = false;
                _slotData.expandAlongNormal = EditorGUILayout.DelayedIntField("Expand Along Normal", _slotData.expandAlongNormal);
                if (GUI.changed)
                {
                    changed = true;
                }
                if (sharedOverlays)
                {
                    List<OverlayData> ovr = GetOverlays();

                    EditorGUILayout.LabelField("Shared Overlays:");
                    GUIHelper.BeginVerticalPadded(10, new Color(0.85f, 0.85f, 0.85f));
                    foreach (OverlayData ov in ovr)
                    {
                        EditorGUILayout.LabelField(ov.asset.overlayName);
                    }
                    GUIHelper.EndVerticalPadded(10);
                }
                else
                {
                    var added = (OverlayDataAsset)EditorGUILayout.ObjectField("Add Overlay", null, typeof(OverlayDataAsset), false);

                    if (added != null)
                    {
                        var newOverlay = new OverlayData(added);
                        _overlayEditors.Add(new OverlayEditor(_recipe, _slotData, newOverlay));
                        _overlayData.Add(newOverlay);
                        _dnaDirty = true;
                        _textureDirty = true;
                        _meshDirty = true;
                        changed = true;
                    }

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
                    }

                    int remapUV = EditorGUILayout.Popup("Remap UV to Main", _slotData.UVSet, new string[] { "None", "UV Set 2", "UV Set 3", "UV Set 4" });
                    if (remapUV != _slotData.UVSet)
                    {
                        _slotData.UVSet = remapUV;
                        _meshDirty = true;
                        changed = true;
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
                            i--;
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
                }
            }
            GUIHelper.EndVerticalPadded(10);

            return changed;
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
