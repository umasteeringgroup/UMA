#define UNITY_EDITOR
#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UMA.Controls;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public class DNAMasterEditor
    {
        //DynamicUMADna:: the following dictionary also needs to use dnaTypeHashes now
        private readonly Dictionary<int, DNASingleEditor> _dnaValues = new Dictionary<int, DNASingleEditor>();
        private readonly int[] _dnaTypeHashes;
        private readonly Type[] _dnaTypes;
        private readonly string[] _dnaTypeNames;
        public int viewDna = 0;
        public UMAData.UMARecipe recipe;
        public static UMAGeneratorBase umaGenerator;

        public DNAMasterEditor(UMAData.UMARecipe recipe)
        {
            this.recipe = recipe;
            UMADnaBase[] allDna = recipe.GetAllDna();

            _dnaTypes = new Type[allDna.Length];
            //DynamicUMADna:: we need the hashes here too
            _dnaTypeHashes = new int[allDna.Length];
            _dnaTypeNames = new string[allDna.Length];

            for (int i = 0; i < allDna.Length; i++)
            {
                var entry = allDna[i];
                var entryType = entry.GetType();

                _dnaTypes[i] = entryType;
                //DynamicUMADna:: we need to use typehashes now
                _dnaTypeHashes[i] = entry.DNATypeHash;
                if (entry is DynamicUMADnaBase)
                {
                    var dynamicDna = entry as DynamicUMADnaBase;
                    if (dynamicDna.dnaAsset != null)
                    {
                        _dnaTypeNames[i] = dynamicDna.dnaAsset.name + " (DynamicUMADna)";
                    }
                }
                else
                {
                    _dnaTypeNames[i] = entryType.Name;
                }
                _dnaValues[entry.DNATypeHash] = new DNASingleEditor(entry);
            }
        }

        public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            GUILayout.BeginHorizontal();
            var newToolBarIndex = EditorGUILayout.Popup("DNA", viewDna, _dnaTypeNames);
            if (newToolBarIndex != viewDna)
            {
                viewDna = newToolBarIndex;
            }
            GUI.enabled = viewDna >= 0;
            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(24)))
            {
                if (viewDna >= 0)
                {
                    //DynamicUMADna:: This needs to use the hash
                    recipe.RemoveDna(_dnaTypeHashes[viewDna]);
                    if (viewDna >= _dnaTypes.Length - 1)
                    {
                        viewDna--;
                    }

                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    _dnaDirty = true;
                    return true;
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();


            if (viewDna >= 0)
            {
                //DynamicUMADna:: We need to use _dnaTypeHashes now
                int dnaTypeHash = _dnaTypeHashes[viewDna];
                var currentDNA = recipe.GetDna(dnaTypeHash);
                if (_dnaValues[dnaTypeHash].OnGUI(currentDNA))
                {
                    _dnaDirty = true;
                    return true;
                }
            }

            return false;
        }

        internal bool NeedsReenable()
        {
            return _dnaValues == null;
        }

        public bool IsValid
        {
            get
            {
                return !(_dnaTypes == null || _dnaTypes.Length == 0);
            }
        }
    }

    public class DNASingleEditor
    {
        private readonly SortedDictionary<string, DNAGroupEditor> _groups = new SortedDictionary<string, DNAGroupEditor>();

        public DNASingleEditor(UMADnaBase dna)
        {
            //DynamicUMADna:: needs a different set up
            if (dna is DynamicUMADnaBase)
            {
                string[] dnaNames = ((DynamicUMADnaBase)dna).Names;
                for (int i = 0; i < dnaNames.Length; i++)
                {
                    string fieldName = ObjectNames.NicifyVariableName(dnaNames[i]);
                    string groupName = "Other";
                    string[] chunks = fieldName.Split(' ');
                    if (chunks.Length > 1)
                    {
                        groupName = chunks[0];
                        fieldName = fieldName.Substring(groupName.Length + 1);
                    }

                    DNAGroupEditor group;
                    _groups.TryGetValue(groupName, out @group);

                    if (group == null)
                    {
                        @group = new DNAGroupEditor(groupName);
                        _groups.Add(groupName, @group);
                    }

                    var entry = new DNAFieldEditor(fieldName, dnaNames[i], dna.GetValue(i), dna);

                    @group.Add(entry);
                }
                foreach (var group in _groups.Values)
                {
                    @group.Sort();
                }
            }
            else
            {
                var fields = dna.GetType().GetFields();

                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType != typeof(float))
                    {
                        continue;
                    }

                    string fieldName;
                    string groupName;
                    GetNamesFromField(field, out fieldName, out groupName);

                    DNAGroupEditor group;
                    _groups.TryGetValue(groupName, out @group);

                    if (group == null)
                    {
                        @group = new DNAGroupEditor(groupName);
                        _groups.Add(groupName, @group);
                    }

                    var entry = new DNAFieldEditor(fieldName, field, dna);

                    @group.Add(entry);
                }

                foreach (var group in _groups.Values)
                {
                    @group.Sort();
                }
            }
        }

        private static void GetNamesFromField(FieldInfo field, out string fieldName, out string groupName)
        {
            fieldName = ObjectNames.NicifyVariableName(field.Name);
            groupName = "Other";

            string[] chunks = fieldName.Split(' ');
            if (chunks.Length > 1)
            {
                groupName = chunks[0];
                fieldName = fieldName.Substring(groupName.Length + 1);
            }
        }

        public bool OnGUI(UMADnaBase currentDNA = null)
        {
            bool changed = false;
            foreach (var dnaGroup in _groups.Values)
            {
                changed |= dnaGroup.OnGUI(currentDNA);
            }

            return changed;
        }
    }

    public class DNAGroupEditor
    {
        private readonly List<DNAFieldEditor> _fields = new List<DNAFieldEditor>();
        private readonly string _groupName;
        private bool _foldout = true;

        public DNAGroupEditor(string groupName)
        {
            _groupName = groupName;
        }

        public bool OnGUI(UMADnaBase currentDNA = null)
        {
            _foldout = EditorGUILayout.Foldout(_foldout, _groupName);

            if (!_foldout)
            {
                return false;
            }

            bool changed = false;

            GUILayout.BeginVertical(EditorStyles.textField);

            foreach (var field in _fields)
            {
                changed |= field.OnGUI(currentDNA);
            }

            GUILayout.EndVertical();

            return changed;
        }

        public void Add(DNAFieldEditor field)
        {
            _fields.Add(field);
        }

        public void Sort()
        {
            _fields.Sort(DNAFieldEditor.comparer);
        }
    }

    public class DNAFieldEditor
    {
        public static Comparer comparer = new Comparer();
        private readonly UMADnaBase _dna;
        private readonly FieldInfo _field;
        //DynamicUmaDna:: requires the following
        private readonly string _realName;
        private readonly string _name;
        private readonly float _value;

        //DynamicUmaDna:: needs a different constructor
        public DNAFieldEditor(string name, string realName, float value, UMADnaBase dna)
        {
            _name = name;
            _realName = realName;
            _dna = dna;

            _value = value;
        }
        public DNAFieldEditor(string name, FieldInfo field, UMADnaBase dna)
        {
            _name = name;
            _field = field;
            _dna = dna;

            _value = (float)field.GetValue(dna);
        }

        public bool OnGUI(UMADnaBase currentDNA = null)
        {
            bool changed = false;
            //With DCS values can get changed when a new recipe is loaded.
            //Check that the value this field currently has matches the value in the recipe
            if (currentDNA != null)
            {
                if (_dna is DynamicUMADnaBase)
                {
                    if (((DynamicUMADnaBase)currentDNA).GetValue(_realName, true) != _value)
                    {
                        changed = true;
                    }
                }
                else
                {
                    if ((float)_field.GetValue(currentDNA) != _value)
                    {
                        changed = true;
                    }
                }
            }

            float newValue = EditorGUILayout.Slider(_name, _value, 0f, 1f);
            //float newValue = EditorGUILayout.FloatField(_name, _value);

            if (newValue != _value)
            {
                //DynamicUmaDna:: we need a different setter
                if (_dna is DynamicUMADnaBase)
                {
                    ((DynamicUMADnaBase)_dna).SetValue(_realName, newValue);
                }
                else
                {
                    _field.SetValue(_dna, newValue);
                }
                changed = true;
            }

            return changed;
        }

        public class Comparer : IComparer<DNAFieldEditor>
        {
            public int Compare(DNAFieldEditor x, DNAFieldEditor y)
            {
                return string.CompareOrdinal(x._name, y._name);
            }
        }
    }

    public class SharedColorsCollectionEditor
    {
        static bool _foldout = true;
        static int selectedChannelCount = 3;//DOS MODIFIED made this three so colors by default have the channels for Gloss/Metallic
        string[] names = new string[16] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16" };
        int[] channels = new int[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        //static bool[] _ColorFoldouts = new bool[0];

        public bool OnGUI(UMAData.UMARecipe _recipe)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            _foldout = EditorGUILayout.Foldout(_foldout, "Shared Colors & Properties");
            GUILayout.EndHorizontal();

            if (_foldout)
            {
                bool changed = false;
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                EditorGUILayout.BeginHorizontal();
                if (_recipe.sharedColors == null)
                {
                    _recipe.sharedColors = new OverlayColorData[0];
                }

                if (_recipe.sharedColors.Length == 0)
                {
                    selectedChannelCount = EditorGUILayout.IntPopup("Channels", selectedChannelCount, names, channels);
                    //DOS these buttons all in a row pushes the UI too wide
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
                else
                {
                    selectedChannelCount = _recipe.sharedColors[0].channelMask.Length;
                }

                if (GUILayout.Button("Add Shared Color"))
                {
                    List<OverlayColorData> sharedColors = new List<OverlayColorData>();
                    sharedColors.AddRange(_recipe.sharedColors);
                    sharedColors.Add(new OverlayColorData(selectedChannelCount));
                    sharedColors[sharedColors.Count - 1].name = "Shared Color " + sharedColors.Count;
                    _recipe.sharedColors = sharedColors.ToArray();
                    changed = true;
                }

                if (GUILayout.Button("Add Shared Color Parms"))
                {
                    List<OverlayColorData> sharedColors = new List<OverlayColorData>();
                    sharedColors.AddRange(_recipe.sharedColors);
                    sharedColors.Add(new OverlayColorData(0));
                    sharedColors[sharedColors.Count - 1].name = "Shared Color " + sharedColors.Count;
                    _recipe.sharedColors = sharedColors.ToArray();
                    changed = true;
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Save Collection"))
                {
                    changed = true;
                }

                //if (_ColorFoldouts.Length != _recipe.sharedColors.Length)
                //	{
                //	Array.Resize<bool>(ref _ColorFoldouts, _recipe.sharedColors.Length);
                //}


                for (int i = 0; i < _recipe.sharedColors.Length; i++)
                {
                    bool del = false;
                    OverlayColorData ocd = _recipe.sharedColors[i];

                    GUIHelper.FoldoutBar(ref _recipe.sharedColors[i].foldout, i + ": " + ocd.name, out del);
                    if (del)
                    {
                        List<OverlayColorData> temp = new List<OverlayColorData>();
                        temp.AddRange(_recipe.sharedColors);
                        temp.RemoveAt(i);
                        _recipe.sharedColors = temp.ToArray();
                        //DOS this wasn't setting changed = true
                        changed = true;
                        // TODO: if all the shared colors are deleted anything that was set to use them
                        //fixed @1022 by checking the shared color still exists
                        break;
                    }
                    if (_recipe.sharedColors[i].foldout)
                    {
                        if (ocd.name == null)
                        {
                            ocd.name = "";
                        }

                        string NewName = EditorGUILayout.DelayedTextField("Name", ocd.name);
                        if (NewName != ocd.name)
                        {
                            ocd.name = NewName;
                            changed = true;
                        }

                        EditorGUILayout.BeginHorizontal();
                        int oldChannelCount = ocd.channelCount;
                        int newChannelCount = EditorGUILayout.IntPopup("Channels", ocd.channelCount, names, channels);

                        if (oldChannelCount != newChannelCount)
                        {
                            ocd.SetChannels(newChannelCount);
                            changed = true;
                        }
                        EditorGUILayout.EndHorizontal();

                        if (ocd.HasColors)
                        {
                            Color NewChannelMask = EditorGUILayout.ColorField("Color Multiplier", ocd.channelMask[0]);
                            if (ocd.channelMask[0] != NewChannelMask)
                            {
                                ocd.channelMask[0] = NewChannelMask;
                                changed = true;
                            }

                            Color NewChannelAdditiveMask = EditorGUILayout.ColorField("Color Additive", ocd.channelAdditiveMask[0]);
                            if (ocd.channelAdditiveMask[0] != NewChannelAdditiveMask)
                            {
                                ocd.channelAdditiveMask[0] = NewChannelAdditiveMask;
                                changed = true;
                            }

                            for (int j = 1; j < ocd.channelMask.Length; j++)
                            {
                                NewChannelMask = EditorGUILayout.ColorField("Texture " + j + " multiplier", ocd.channelMask[j]);
                                if (ocd.channelMask[j] != NewChannelMask)
                                {
                                    ocd.channelMask[j] = NewChannelMask;
                                    changed = true;
                                }

                                NewChannelAdditiveMask = EditorGUILayout.ColorField("Texture " + j + " additive", ocd.channelAdditiveMask[j]);
                                if (ocd.channelAdditiveMask[j] != NewChannelAdditiveMask)
                                {
                                    ocd.channelAdditiveMask[j] = NewChannelAdditiveMask;
                                    changed = true;
                                }
                            }
                            if (ocd.PropertyBlock == null)
                            {
                                if (GUILayout.Button("Add Shader Property Block"))
                                {
                                    ocd.PropertyBlock = new UMAMaterialPropertyBlock();
                                }
                            }
                        }
                        if (ocd.PropertyBlock != null)
                        {
                            if (GUILayout.Button("Remove Shader Property Block"))
                            {
                                ocd.PropertyBlock = null;
                            }
                            else
                            {
                                changed |= UMAMaterialPropertyBlockDrawer.OnGUI(ocd.PropertyBlock);
                            }
                        }
                    }
                }
                GUIHelper.EndVerticalPadded(3);
                return changed;
            }
            return false;
        }
    }

    public static class TagsEditor
    {
        public static Dictionary<string, bool> _foldout = new Dictionary<string, bool>();

        public static string[] RaceNames = null;


        public static void DoRaceGUI(ref bool Changed, SlotData slotData)
        {
            if (slotData.Races == null)
            {
                slotData.Races = new string[0];
            }
            if (true)
            {
                GUILayout.Space(10);
                GUILayout.Label("Only add for these Races:");
                // do the race matches here.
                if (RaceNames == null)
                {
                    List<string> theRaceNames = new List<string>();
                    RaceData[] races = UMAContextBase.Instance.GetAllRaces();
                    foreach (RaceData race in races)
                    {
                        if (race != null)
                        {
                            theRaceNames.Add(race.raceName);
                        }
                    }
                    RaceNames = theRaceNames.ToArray();
                }
                GUILayout.BeginHorizontal();
                if (!SlotEditor.SelectedRace.ContainsKey(slotData.slotName))
                {
                    SlotEditor.SelectedRace.Add(slotData.slotName, 0);
                }

                SlotEditor.SelectedRace[slotData.slotName] = EditorGUILayout.Popup(SlotEditor.SelectedRace[slotData.slotName], RaceNames, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Add Race"))
                {
                    // Add the selected race name if it's not already there.
                    string theRace = RaceNames[SlotEditor.SelectedRace[slotData.slotName]];
                    List<string> Races = new List<string>(slotData.Races);
                    if (!Races.Contains(theRace))
                    {
                        Races.Add(theRace);
                        slotData.Races = Races.ToArray();
                        Changed = true;
                    }
                }
                GUILayout.EndHorizontal();

                DoTagsDisplay(ref slotData.Races, ref Changed);

                EditorGUI.BeginChangeCheck();
                slotData.isSwapSlot = EditorGUILayout.Toggle("This is a swap slot", slotData.isSwapSlot);
                if (slotData.isSwapSlot)
                {
                    EditorGUILayout.HelpBox("A Swap slot will only be added if there is a slot with the below tag already in the recipe. If there is no slot with the tag then this slot will not be added.", MessageType.Info);
                    string newSwapTag = CharacterBaseEditor.DoTagSelector(slotData.swapTag);
                    if (!string.IsNullOrEmpty(newSwapTag))
                    {
                        slotData.swapTag = newSwapTag;
                        Changed = true;
                    }
                    slotData.swapTag = EditorGUILayout.DelayedTextField("Swap slot(s) with this tag", slotData.swapTag);
                }
                else
                {
                    slotData.swapTag = "";
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Changed = true;
                }
            }
        }

        const string focusctrl = "TheButtonThatNeedsToFocusSoTheTextInTheTextBoxDisappears";
        public static string DoTagsGUI(ref bool Changed, string TempTag, SlotData slotData)
        {
            string slotName = slotData.slotName;

            if (!_foldout.ContainsKey(slotName))
            {
                _foldout.Add(slotName, false);
            }

            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            _foldout[slotName] = EditorGUILayout.Foldout(_foldout[slotName], "Matching Criteria");
            GUILayout.EndHorizontal();
            if (_foldout[slotName])
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
                if (slotData.asset.isWildCardSlot)
                {
                    GUILayout.Label("Match Tags:");
                }
                else
                {
                    GUILayout.Label("Edit tags for this slot:");
                }
                
                string newTag = CharacterBaseEditor.DoTagSelector(slotData.asset.tags);
                if (!string.IsNullOrEmpty(newTag))
                {
                    Changed |= AddSlotTag(newTag, slotData);
                }
                //EditorGUILayout.HelpBox("Tags GUI here...", MessageType.Info);
                if (slotData.tags == null)
                {
                    slotData.tags = new string[0];
                }
                if (slotData.Races == null)
                {
                    slotData.Races = new string[0];
                }
                
                GUILayout.BeginHorizontal();
                TempTag = EditorGUILayout.TextField(TempTag, GUILayout.ExpandWidth(true));
                GUI.SetNextControlName(focusctrl);
                if (GUILayout.Button("x", GUILayout.Width(18)))
                {
                    TempTag = "";
                    GUI.FocusControl(focusctrl);
                }
                if (GUILayout.Button("Add Tag"))
                {
                    if (!string.IsNullOrWhiteSpace(TempTag))
                    {
                        Changed |= AddSlotTag(TempTag, slotData);
                    }
                }
                if (GUILayout.Button("Clear"))
                {
                    slotData.tags = new string[0];
                    Changed = true;
                }
                if (GUILayout.Button("Load"))
                {
                    string fname = EditorUtility.OpenFilePanel("Load", "", "txt");
                    {
                        if (!string.IsNullOrEmpty(fname))
                        {
                            slotData.tags = File.ReadAllLines(fname);
                            Changed = true;
                        }
                    }
                }
                if (GUILayout.Button("Save"))
                {
                    string fname = EditorUtility.SaveFilePanel("Save", "", "Tags", "txt");
                    {
                        if (!string.IsNullOrEmpty(fname))
                        {
                            File.WriteAllLines(fname, slotData.tags);
                        }
                    }
                }

                GUILayout.EndHorizontal();

                DoTagsDisplay(ref slotData.tags, ref Changed);
                
                //				if (slotData.asset.isWildCardSlot)
                if (true)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Only add for these Races:");
                    // do the race matches here.
                    if (RaceNames == null)
                    {
                        List<string> theRaceNames = new List<string>();
                        RaceData[] races = UMAContextBase.Instance.GetAllRaces();
                        foreach (RaceData race in races)
                        {
                            if (race != null)
                            {
                                theRaceNames.Add(race.raceName);
                            }
                        }
                        RaceNames = theRaceNames.ToArray();
                    }
                    GUILayout.BeginHorizontal();
                    if (!SlotEditor.SelectedRace.ContainsKey(slotData.slotName))
                    {
                        SlotEditor.SelectedRace.Add(slotData.slotName, 0);
                    }

                    SlotEditor.SelectedRace[slotData.slotName] = EditorGUILayout.Popup(SlotEditor.SelectedRace[slotData.slotName], RaceNames, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Add Race"))
                    {
                        // Add the selected race name if it's not already there.
                        string theRace = RaceNames[SlotEditor.SelectedRace[slotData.slotName]];
                        List<string> Races = new List<string>(slotData.Races);
                        if (!Races.Contains(theRace))
                        {
                            Races.Add(theRace);
                            slotData.Races = Races.ToArray();
                            Changed = true;
                        }
                    }
                    GUILayout.EndHorizontal();

                    DoTagsDisplay(ref slotData.Races, ref Changed);

                    EditorGUI.BeginChangeCheck();
                    slotData.isSwapSlot = EditorGUILayout.Toggle("This is a swap slot", slotData.isSwapSlot);
                    if (slotData.isSwapSlot)
                    {
                        EditorGUILayout.HelpBox("A Swap slot will only be added if there is a slot with the below tag already in the recipe. If there is no slot with the tag then this slot will not be added.", MessageType.Info);
                        string newSwapTag = CharacterBaseEditor.DoTagSelector(slotData.swapTag);
                        if (!string.IsNullOrEmpty(newSwapTag))
                        {
                            slotData.swapTag = newSwapTag;
                            Changed = true;
                        }
                        slotData.swapTag = EditorGUILayout.DelayedTextField("Swap slot(s) with this tag", slotData.swapTag);
                    }
                    else
                    {
                        slotData.swapTag = "";
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        Changed = true;
                    }
                }
                GUIHelper.EndVerticalPadded(10);
            }
            return TempTag;
        }

        private static bool AddSlotTag(string TempTag, SlotData slotData)
        {
            bool Changed = false;
            var tagList = new List<string>(slotData.tags);
            if (!tagList.Contains(TempTag))
            {
                tagList.Add(TempTag);
                slotData.tags = tagList.ToArray();
                Changed = true;
            }

            return Changed;
        }

        public static int DoTagsDisplay(ref string[] tags, ref bool changed)
        {
            int deleted = -1;

            for (int i = 0; i < tags.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(tags[i], EditorStyles.textField, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("X", GUILayout.Width(16)))
                {
                    deleted = i;
                }
                GUILayout.EndHorizontal();
            }
            if (deleted > -1)
            {
                var tagList = new List<string>(tags);
                tagList.RemoveAt(deleted);
                tags = tagList.ToArray();
                changed = true;
            }
            return -1;
        }
    }

    public class SlotMasterEditor
    {
        // the last slot dropped. Must live between instances.
        public static string LastSlot = "";
        // track open/closed here. Must live between instances.
        public static Dictionary<string, bool> OpenSlots = new Dictionary<string, bool>();

        //DOS Changed this to protected so childs can inherit
        protected readonly UMAData.UMARecipe _recipe;
        //DOS Changed this to protected so childs can inherit
        protected readonly List<SlotEditor> _slotEditors = new List<SlotEditor>();
        //DOS Changed this to protected so childs can inherit
        protected readonly SharedColorsCollectionEditor _sharedColorsEditor = new SharedColorsCollectionEditor();
        //an Id for the 'ClickToPick' slot picker
        protected static int _slotPickerID = -1;

        protected List<SlotDataAsset> DraggedSlots = new List<SlotDataAsset>();
        protected List<OverlayDataAsset> DraggedOverlays = new List<OverlayDataAsset>();


        /// <summary>
        /// Add the drag and drop files to the recipe.
        /// if any overlays are dropped, they are added to the first slot that was dropped.
        /// if no slots were dropped, they are added to the first slot in the recipe.
        /// </summary>
        protected void AddDraggedFiles()
        {
            SlotData FirstSlot = null;

            // Add the slots.
            // if there are overlays, well, no way to really know where they go, so add them to the first slot.
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

        //DOS Changed this to protected so childs can inherit
        protected bool DropAreaGUI(Rect dropArea)
        {
            var evt = Event.current;
            int pickedCount = 0;
            bool recipesMerged = false;
            //make the box clickable so that the user can select slotData assets from the asset selection window
            //TODO if we can make this so you can click multiple slots to add them make this the only option and get rid of the object selection 'field'
            if (evt.type == EventType.MouseUp)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    _slotPickerID = EditorGUIUtility.GetControlID(new GUIContent("slotObjectPicker"), FocusType.Passive);
                    EditorGUIUtility.ShowObjectPicker<SlotDataAsset>(null, false, "", _slotPickerID);
                    Event.current.Use();//stops the Mismatched LayoutGroup errors
                                        //return false;//true/false notsure
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
                    Event.current.Use();//stops the Mismatched LayoutGroup errors
                                        //return true;
                }
                else
                {
                    Event.current.Use();//stops the Mismatched LayoutGroup errors
                }
                //return false;//true/false notsure
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
                                // AddSlotDataAsset(tempSlotDataAsset);
                                continue;
                            }
                            if (draggedObjects[i] is OverlayDataAsset)
                            {
                                DraggedOverlays.Add(draggedObjects[i] as OverlayDataAsset);
                            }
                            if (draggedObjects[i] is UMATextRecipe)
                            {
                                var textRecipe = draggedObjects[i] as UMATextRecipe;
                                var recipe = textRecipe.GetCachedRecipe(UMAContextBase.Instance);
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

        //DOS Changed this to protected so childs can inherit
        protected void AddSlotDataAsset(SlotDataAsset added)
        {
            var slot = new SlotData(added);
            _recipe.MergeSlot(slot, false);
        }

        //DOS Changed this to protected so childs can inherit
        protected void RecursiveScanFoldersForAssets(string path)
        {
            var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
            foreach (var assetFile in assetFiles)
            {
                var tempSlotDataAsset = AssetDatabase.LoadAssetAtPath(assetFile, typeof(SlotDataAsset)) as SlotDataAsset;
                if (tempSlotDataAsset)
                {
                    DraggedSlots.Add(tempSlotDataAsset);
                    //AddSlotDataAsset(tempSlotDataAsset);
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
            if (UMAContextBase.Instance != null)
            {
                if (UMAContextBase.Instance.HasRace(_raceData.raceName) != null)
                {
                    return true;
                }
            }

            AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<RaceData>(_raceData.raceName);
            if (ai != null)
            {
                return true;
            }

            return false;
        }

        public SlotMasterEditor(UMAData.UMARecipe recipe)
        {
            _recipe = recipe;

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

                _slotEditors.Add(new SlotEditor(_recipe, slot, i));
            }

            if (_slotEditors.Count > 1)
            {
                // Don't juggle the order - this way, they're in the order they're in the file, or dropped in.
                List<SlotEditor> sortedSlots = new List<SlotEditor>(_slotEditors);
                sortedSlots.Sort(SlotEditor.comparer);


                // previous code didn't work when there were only two slots
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
        //DOS made this virtual so children can override
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
                changed = true;
            }

            if (_recipe.raceData != null && !RaceInIndex(_recipe.raceData))
            {
                EditorGUILayout.HelpBox("Race " + _recipe.raceData.raceName + " is not indexed! Either assign it to an assetBundle or use one of the buttons below to add it to the Scene/Global Library.", MessageType.Error);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add to Scene Only"))
                {
                    UMAContextBase.Instance.AddRace(_recipe.raceData);
                }
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

            var baseSlotsList = new List<SlotData>();
            var baseSlotsNamesList = new List<string>() { "None" };
            if (_recipe.raceData != null)
            {
                if (_recipe.raceData.baseRaceRecipe != null)
                {
                    //check this recipe is NOT the actual race base recipe
                    if (_recipe.raceData.baseRaceRecipe.name != targetName)
                    {
                        //we dont want to show this if this IS the base recipe
                        UMAData.UMARecipe thisBaseRecipe = _recipe.raceData.baseRaceRecipe.GetCachedRecipe(UMAContextBase.Instance);
                        SlotData[] thisBaseSlots = thisBaseRecipe.GetAllSlots();
                        foreach (SlotData slot in thisBaseSlots)
                        {
                            if (slot != null)
                            {
                                baseSlotsList.Add(slot);
                                baseSlotsNamesList.Add(slot.slotName);
                            }
                        }
                    }
                }
                if (baseSlotsNamesList.Count > 1)
                {
                    EditorGUI.BeginChangeCheck();
                    var baseAdded = EditorGUILayout.Popup("Add Base Slot", 0, baseSlotsNamesList.ToArray());
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (baseAdded != 0)
                        {
                            var slot = baseSlotsList[baseAdded - 1];
                            LastSlot = slot.asset.slotName;
                            var slotToAdd = new SlotData(slot.asset);
                            _recipe.MergeSlot(slotToAdd, false);
                            changed |= true;
                            _dnaDirty |= true;
                            _textureDirty |= true;
                            _meshDirty |= true;
                        }
                    }
                }
            }

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

            //check the slotEditors are uptodate with the slots in the recipe
            //They can get out of sync happen in the UMAData component when DCS modifies the recipe
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
                if (slotData != null)
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
            if (UMAContextBase.Instance != null)
            {
                if (UMAContextBase.Instance.HasSlot(_slotData.asset.slotName))
                {
                    return true;
                }
            }

            AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<SlotDataAsset>(_slotData.asset.slotName);
            if (ai != null)
            {
                return true;
            }

            return false;
        }

        public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            bool delete;
            bool select;
            bool _foldOut = FoldOut;

            GUIHelper.FoldoutBarButton(ref _foldOut, _name + "      (" + _slotData.asset.name + ")", "inspect", out select, out delete);

            FoldOut = _foldOut;

            // Set this before exiting.
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
                if (GUILayout.Button("Add to Scene Only"))
                {
                    UMAContextBase.Instance.AddSlotAsset(_slotData.asset);
                }
                if (GUILayout.Button("Add to Global Index (Recommended)"))
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), _slotData.asset);
                    UMAAssetIndexer.Instance.ForceSave();
                }
                GUILayout.EndHorizontal();
            }

            bool disabled = _slotData.isDisabled;
            _slotData.isDisabled = EditorGUILayout.Toggle("Disabled", _slotData.isDisabled);

            if (disabled != _slotData.isDisabled)
            {
                changed = true;
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
                    EditorGUILayout.HelpBox("Override Target Tag and Smooshed Tag are used to override the default tags to find the target and smooshed slots. This is useful if you have multiple clipping planes and want to use different tags for each one. By default, the target is " +
                        " 'Smooshtarget' and the smooshed slot is 'Smooshable'", MessageType.Info);
                    _slotData.smooshTargetTag = EditorGUILayout.TextField("Override Target Tag", _slotData.smooshTargetTag);
                    _slotData.smooshableTag = EditorGUILayout.TextField("Override Smooshed Tag", _slotData.smooshableTag);

                    changed = EditorGUI.EndChangeCheck();

                }
            }
            else
            {
                #region Blendshape Slot
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
                            // show slots
                            // show x (delete)
                            // add an object box to add one.
                            GUILayout.Label(sda.slotName, EditorStyles.textField, GUILayout.ExpandWidth(true));
                            if (GUILayout.Button("X", GUILayout.Width(22)))
                            {
                                _recipe.RemoveSlot(sda);
                                wasDeleted = true;
                            }
                            GUILayout.EndHorizontal();
                        }
                        //
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
                #endregion

                #region TAGS EDITOR
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
                //TemporarySlotTags[_slotData.slotName] = TagsEditor.DoTagsGUI(ref changed, TemporarySlotTags[_slotData.slotName], _slotData);


                #endregion

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

    public class OverlayEditor
    {
        public static Dictionary<string, bool> OverlayExpanded = new Dictionary<string, bool>();
        private readonly UMAData.UMARecipe _recipe;
        protected readonly SlotData _slotData;
        private readonly OverlayData _overlayData;
        private OverlayDataAsset _baseOverlayData;
        private readonly TextureEditor[] _textures;
        private ColorEditor[] _colors;
        private bool isUV = false;


        public OverlayData Overlay
        {
            get { return _overlayData; }
        }

#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
		private ProceduralPropertyEditor[] _properties;
		private ProceduralPropertyDescription[] _descriptions;
		private int _selectedProperty = 0;
#endif
        private bool _foldout = true;

        public bool Delete { get; private set; }

        public int move;
        private static OverlayData showExtendedRangeForOverlay;

        public void EnsureEntry(string overlayName)
        {
            if (OverlayExpanded.ContainsKey(overlayName))
            {
                return;
            }

            OverlayExpanded.Add(overlayName, true);
        }

        public OverlayEditor(UMAData.UMARecipe recipe, SlotData slotData, OverlayData overlayData, OverlayDataAsset baseOverlayDataAsset = null)
        {
            _recipe = recipe;
            _overlayData = overlayData;
            _slotData = slotData;
            _baseOverlayData = baseOverlayDataAsset;
            EnsureEntry(overlayData.overlayName);

            if ((_overlayData.rect.x <= 1.0f) && (_overlayData.rect.y <= 1.0f) && (_overlayData.rect.width <= 1.0f) && (_overlayData.rect.height <= 1.0f))
            {
                isUV = true;
            }

            // Sanity check the colors
            if (_recipe.sharedColors == null)
            {
                _recipe.sharedColors = new OverlayColorData[0];
            }
            else
            {
                for (int i = 0; i < _recipe.sharedColors.Length; i++)
                {
                    OverlayColorData ocd = _recipe.sharedColors[i];
                    if (!ocd.HasName())
                    {
                        ocd.name = "Shared Color " + (i + 1);
                    }
                }
            }

            _textures = new TextureEditor[overlayData.asset.textureCount];
            for (int i = 0; i < overlayData.asset.textureCount; i++)
            {
                _textures[i] = new TextureEditor(overlayData.textureArray[i], i, overlayData);
            }

            BuildColorEditors();

        }

        private void BuildColorEditors()
        {
            _overlayData.Validate();

            if (_overlayData.colorData == null || _overlayData.colorData.channelMask == null)
            {
                return;
            }

            _colors = new ColorEditor[_overlayData.colorData.channelMask.Length * 2];

            for (int i = 0; i < _overlayData.colorData.channelMask.Length; i++)
            {
                _colors[i * 2] = new ColorEditor(
                   _overlayData.colorData.channelMask[i],
                   string.Format(i == 0
                      ? "Color multiplier"
                      : "Texture {0} multiplier", i));

                _colors[(i * 2) + 1] = new ColorEditor(
                   _overlayData.colorData.channelAdditiveMask[i],
                   string.Format(i == 0
                      ? "Color additive"
                      : "Texture {0} additive", i));
            }
        }

        private bool InIndex(OverlayData _overlayData)
        {
            if (UMAContextBase.Instance != null)
            {
                if (UMAContextBase.Instance.HasOverlay(_overlayData.overlayName))
                {
                    return true;
                }
            }

            AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<OverlayDataAsset>(_overlayData.asset.overlayName);
            if (ai != null)
            {
                return true;
            }

            return false;
        }

        public bool OnGUI()
        {
            List<string> buttons = new List<string>() { "Inspect","Mat","UMat" };
            List<bool> pressed = new List<bool>() { false, false, false };
            bool delete;

            _foldout = OverlayExpanded[_overlayData.overlayName];


            int queue = 0;
            string matName = "Unknown";
            if (_overlayData.asset.material != null)
            {
                matName = _overlayData.asset.material.name;
                queue = _overlayData.asset.material.material.renderQueue;
            }


            GUIHelper.FoldoutBarButton(ref _foldout, $"{_overlayData.asset.overlayName} ( {matName} Q:{queue})", buttons,out pressed, out move, out delete);

            if (pressed[0])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.GetInstanceID());
                InspectorUtlity.InspectTarget(_overlayData.asset);
            }

            if (pressed[1])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.material.material.GetInstanceID());
                InspectorUtlity.InspectTarget(_overlayData.asset.material.material);
            }

            if (pressed[2])
            {
                EditorGUIUtility.PingObject(_overlayData.asset.material.GetInstanceID());
                InspectorUtlity.InspectTarget(_overlayData.asset.material);
            }


            OverlayExpanded[_overlayData.overlayName] = _foldout;
            Delete = delete;

            if (!_foldout)
            {
                return false;
            }

            GUIHelper.BeginHorizontalPadded(10, Color.white);
            GUILayout.BeginVertical();



            if (!InIndex(_overlayData))
            {
                EditorGUILayout.HelpBox("Overlay " + _overlayData.asset.name + " is not indexed!", MessageType.Error);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add to Scene Only"))
                {
                    UMAContextBase.Instance.AddOverlayAsset(_overlayData.asset);

                }
                if (GUILayout.Button("Add to Global Index"))
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(OverlayDataAsset), _overlayData.asset);
                    UMAAssetIndexer.Instance.ForceSave();
                }
                GUILayout.EndHorizontal();
            }

            _overlayData.Validate();

            bool changed = false;

            if (!isUV)
            {
                EditorGUILayout.HelpBox("Overlay " + _overlayData.asset.name + " is not using UV coordinates! Convert?", MessageType.Error);
                _overlayData.editorReferenceTextureSize = EditorGUILayout.Vector2Field("Reference Texture Size", _overlayData.editorReferenceTextureSize);
                if (_overlayData.editorReferenceTextureSize.magnitude != 0.0f)
                { 
                    if (GUILayout.Button("Convert to UV"))
                    {
                        _overlayData.rect = new Rect(_overlayData.rect.x / _overlayData.editorReferenceTextureSize.x, _overlayData.rect.y / _overlayData.editorReferenceTextureSize.y, _overlayData.rect.width / _overlayData.editorReferenceTextureSize.x, _overlayData.rect.height / _overlayData.editorReferenceTextureSize.y);
                        changed = true;
                    }
                }
            }
            if (_slotData.asset.material != null && _overlayData.asset.material != null)
            {
                if (_overlayData.asset.material.name != _slotData.material.name)
                {
                    if (_overlayData.asset.material.channels.Length == _slotData.material.channels.Length)
                    {
                        EditorGUILayout.HelpBox("Material " + _overlayData.asset.material.name + " does not match slot material: " + _slotData.material.name, MessageType.Error);
                        if (GUILayout.Button("Copy Slot Material to Overlay"))
                        {
                            _overlayData.asset.material = _slotData.asset.material;
                            EditorUtility.SetDirty(_overlayData.asset);
                            string path = AssetDatabase.GetAssetPath(_overlayData.asset.GetInstanceID());
                            AssetDatabase.ImportAsset(path);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Material " + _overlayData.asset.material.name + " does not match slot material: " + _slotData.asset.material.name + " and Channel count is not the same. Overlay must be removed or fixed manually", MessageType.Error);
                    }
                    if (GUILayout.Button("Select Slot in Project"))
                    {
                        // find the asset.
                        // select it in the project.
                        Selection.activeObject = _slotData.asset;
                    }

                    if (GUILayout.Button("Select Overlay in Project"))
                    {
                        // find the asset.
                        // select it in the project.
                        Selection.activeObject = _overlayData.asset;
                    }
                }
            }

            // Edit the colors
            changed |= OnColorGUI();


            // do tags gui here
            //
            // Edit the transformations
            changed |= OnTagsGUI();

            bool originalInstanceTransformed = _overlayData.instanceTransformed;
            float originalRotation = _overlayData.Rotation;
            Vector2 originalScale = _overlayData.Scale;
            Vector2 originalTranslate = _overlayData.Translate;

            if (_overlayData.asset.material != null && _overlayData.asset.material.materialType == UMAMaterial.MaterialType.UseExistingTextures)
            {
                int useUV = EditorGUILayout.Popup("UV Set for this overlay", _overlayData.UVSet, new string[] { "No Change", "UV Set 1", "UV Set 2", "UV Set 3" });
                if (useUV != _overlayData.UVSet)
                {
                    _overlayData.UVSet = useUV;
                    changed = true;
                }
            }
            else
            {
                if (_overlayData.UVSet != 0) 
                {
                    _overlayData.UVSet = 0;
                    changed = true;
                }
            }
            _overlayData.instanceTransformed = GUILayout.Toggle(_overlayData.instanceTransformed, "Transform");
            if (_overlayData.instanceTransformed)
            {
                GUIHelper.BeginVerticalPadded(5, new Color(1, 1, 1, 1));
                EditorGUILayout.HelpBox("Warning: translating, scaling or rotation could result in writing outside the bounds of the texture on the atlas. Be sure to use only in safe areas.", MessageType.Info);
                _overlayData.Rotation = EditorGUILayout.FloatField("Rotation", _overlayData.Rotation);
                _overlayData.Scale = EditorGUILayout.Vector2Field("Scale", _overlayData.Scale);
                EditorGUILayout.LabelField("Translation: ");
                _overlayData.Translate.x = EditorGUILayout.Slider("X:",_overlayData.Translate.x * 100.0f, -100.0f, 100.0f) / 100.0f;
                _overlayData.Translate.y = EditorGUILayout.Slider("Y:", _overlayData.Translate.y * 100.0f, -100.0f, 100.0f) / 100.0f;
                GUIHelper.EndVerticalPadded(5);
            }

            if (_overlayData.instanceTransformed != originalInstanceTransformed)
            {
                changed = true;
            }

            if (_overlayData.Rotation != originalRotation)
            {
                changed = true;
            }

            if (_overlayData.Scale != originalScale)
            {
                changed = true;
            }
            if (_overlayData.Translate != originalTranslate)
            {
                changed = true;
            }


            // Edit the rect
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rect");
            if (!isUV && _baseOverlayData != null)
            {
                if (GUILayout.Button("Convert to UV"))
                {
                    _overlayData.rect.x /= _baseOverlayData.textureList[0].width;
                    _overlayData.rect.width /= _baseOverlayData.textureList[0].width;
                    _overlayData.rect.y /= _baseOverlayData.textureList[0].height;
                    _overlayData.rect.height /= _baseOverlayData.textureList[0].height;
                    isUV = true;
                }
            }
            GUILayout.EndHorizontal();


            Rect Save = _overlayData.rect;
            if (!isUV)
            {
                _overlayData.rect = EditorGUILayout.RectField(_overlayData.rect);
            }
            else
            {
                GUILayout.BeginHorizontal(); // x,y
                EditorGUILayout.LabelField("X:", GUILayout.Width(24));
                _overlayData.rect.x = EditorGUILayout.Slider(_overlayData.rect.x * 100.0f, 0.0f, 100.0f) / 100.0f;
                EditorGUILayout.LabelField("Y:", GUILayout.Width(24));
                _overlayData.rect.y = EditorGUILayout.Slider(_overlayData.rect.y * 100.0f, 0.0f, 100.0f) / 100.0f;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(); // w,h
                EditorGUILayout.LabelField("W:", GUILayout.Width(24));
                _overlayData.rect.width = EditorGUILayout.Slider(_overlayData.rect.width * 100.0f, 0.0f, 100.0f) / 100.0f;
                EditorGUILayout.LabelField("H:", GUILayout.Width(24));
                _overlayData.rect.height = EditorGUILayout.Slider(_overlayData.rect.height * 100.0f, 0.0f, 100.0f) / 100.0f;
                GUILayout.EndHorizontal();
            }
            if (Save.x != _overlayData.rect.x || Save.y != _overlayData.rect.y || Save.width != _overlayData.rect.width || Save.height != _overlayData.rect.height)
            {
                changed = true;
            }




            // Edit the textures
            GUILayout.Label("Textures");
            GUILayout.BeginHorizontal();
            foreach (var texture in _textures)
            {
                changed |= texture.OnGUI(true);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            foreach (var texture in _textures)
            {
                changed |= texture.OnBlendGUI();
            }
            if (_overlayData.asset.material != null && _overlayData.asset.material.materialType == UMAMaterial.MaterialType.UseExistingTextures)
            {
                foreach (var texture in _textures)
                {
                    changed |= texture.OnTileGUI();
                }
            }
            GUILayout.EndHorizontal();


            GUILayout.EndVertical();

            GUIHelper.EndVerticalPadded(10);

            return changed;
        }
        
                private bool OnTagsGUI()
        {
            bool changed = false;
            if (_overlayData.tags == null)
            {
                _overlayData.tags = new string[0];
            }

            if (_overlayData.tags.Length == 0)
            {
                EditorGUILayout.HelpBox("No tags defined for this overlay", MessageType.Info);
            }

            string newTag = CharacterBaseEditor.DoTagSelector(_overlayData.tags);
            if (!string.IsNullOrWhiteSpace(newTag))
            {
                changed = true;
                Array.Resize(ref _overlayData.tags, _overlayData.tags.Length + 1);
                _overlayData.tags[_overlayData.tags.Length - 1] = newTag;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tags");
            if (GUILayout.Button("Add Empty"))
            {
                Array.Resize(ref _overlayData.tags, _overlayData.tags.Length + 1);
                _overlayData.tags[_overlayData.tags.Length - 1] = "";
                changed = true;
            }
            GUILayout.EndHorizontal();

            int deleted = -1;
            for (int i = 0; i < _overlayData.tags.Length; i++)
            {
                GUILayout.BeginHorizontal();
                _overlayData.tags[i] = EditorGUILayout.TextField(_overlayData.tags[i]);
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    deleted = i;
                }
                GUILayout.EndHorizontal();
            }
            if (deleted != -1)
            {
                changed = true;
                List<string> tags = new List<string>(_overlayData.tags);
                tags.RemoveAt(deleted);
                _overlayData.tags = tags.ToArray();
            }
            return changed;
        }

        public bool OnColorGUI()
        {
            bool changed = false;
            int currentsharedcol = 0;
            List<string> propertyNames = new List<string>();
            Dictionary<int, int> PropertyPosition = new Dictionary<int, int>();
            string[] sharednames = new string[_recipe.sharedColors.Length];


            if (_overlayData.isEmpty)
            {
                int foundProperty = -1;

                for (int i = 0; i < _recipe.sharedColors.Length; i++)
                {
                    if (_recipe.sharedColors[i].channelCount == 0)
                    {
                        int currentPropertyIndex = propertyNames.Count;

                        if (foundProperty == -1)
                        {
                            foundProperty = currentPropertyIndex;
                            //changed = true;
                        }

                        propertyNames.Add(_recipe.sharedColors[i].name);
                        PropertyPosition.Add(currentPropertyIndex, i);
                        if (_overlayData.colorData.GetHashCode() == _recipe.sharedColors[i].GetHashCode())
                        {
                            foundProperty = currentPropertyIndex;
                        }
                    }
                }


                if (propertyNames.Count > 0)
                {
                    if (foundProperty == -1)
                    {
                        foundProperty = 0;
                        changed = true;
                    }
                    GUIHelper.BeginVerticalPadded(2f, new Color(0.75f, 0.875f, 1f));
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Select property name");
                    int prevcol = foundProperty;
                    int newprop = EditorGUILayout.Popup(foundProperty, propertyNames.ToArray());

                    GUILayout.EndHorizontal();
                    GUIHelper.EndVerticalPadded(2f);
                    GUILayout.Space(2f);
                    if (newprop != foundProperty || changed == true)
                    {
                        changed = true;
                        int proppos = PropertyPosition[newprop];
                        _overlayData.colorData = _recipe.sharedColors[proppos];
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Add a property to the shared color above to be able to associate a name with this overlay and assign properties at runtime", MessageType.Info);
                }
                return changed;
            }

            //DOS 13012016 if we also check here that _recipe.sharedColors still contains 
            //the desired ocd then we can save the collection when colors are deleted
            if (_overlayData.colorData.IsASharedColor && _recipe.HasSharedColor(_overlayData.colorData))
            {

                bool found = false;
                GUIHelper.BeginVerticalPadded(2f, new Color(0.75f, 0.875f, 1f));
                GUILayout.BeginHorizontal();

                if (GUILayout.Toggle(true, "Use Shared Color") == false)
                {
                    // Unshare color
                    _overlayData.colorData = _overlayData.colorData.Duplicate();
                    _overlayData.colorData.name = OverlayColorData.UNSHARED;
                    changed = true;
                }
                else
                {
                    for (int i = 0; i < _recipe.sharedColors.Length; i++)
                    {
                        sharednames[i] = i + ": " + _recipe.sharedColors[i].name;
                        if (_overlayData.colorData.GetHashCode() == _recipe.sharedColors[i].GetHashCode())
                        {
                            currentsharedcol = i;
                            found = true;
                        }
                    }

                    int newcol = EditorGUILayout.Popup(currentsharedcol, sharednames);
                    if (newcol != currentsharedcol || !found)
                    {
                        changed = true;
                        _overlayData.colorData = _recipe.sharedColors[newcol];
                    }
                }
                GUILayout.EndHorizontal();
                GUIHelper.EndVerticalPadded(2f);
                GUILayout.Space(2f);
                return changed;

            }
            else
            {
                GUIHelper.BeginVerticalPadded(2f, new Color(0.75f, 0.875f, 1f));
                GUILayout.BeginHorizontal();

                if (_recipe.sharedColors.Length > 0)
                {
                    if (GUILayout.Toggle(false, "Use Shared Color"))
                    {
                        _overlayData.colorData = _recipe.sharedColors[0];
                        changed = true;
                    }
                }

                GUILayout.EndHorizontal();

                bool showExtendedRanges = showExtendedRangeForOverlay == _overlayData;
                var newShowExtendedRanges = EditorGUILayout.Toggle("Show Extended Ranges", showExtendedRanges);

                if (showExtendedRanges != newShowExtendedRanges)
                {
                    if (newShowExtendedRanges)
                    {
                        showExtendedRangeForOverlay = _overlayData;
                    }
                    else
                    {
                        showExtendedRangeForOverlay = null;
                    }
                }

                for (int k = 0; k < _colors.Length; k++)
                {
                    Color color;
                    if (showExtendedRanges && k % 2 == 0)
                    {
                        Vector4 colorVector = new Vector4(_colors[k].color.r, _colors[k].color.g, _colors[k].color.b, _colors[k].color.a);
                        colorVector = EditorGUILayout.Vector4Field(_colors[k].description, colorVector);
                        color = new Color(colorVector.x, colorVector.y, colorVector.z, colorVector.w);
                    }
                    else
                    {
                        color = EditorGUILayout.ColorField(_colors[k].description, _colors[k].color);
                    }

                    if (color.r != _colors[k].color.r ||
                     color.g != _colors[k].color.g ||
                     color.b != _colors[k].color.b ||
                     color.a != _colors[k].color.a)
                    {
                        if (k % 2 == 0)
                        {
                            _overlayData.colorData.channelMask[k / 2] = color;
                        }
                        else
                        {
                            _overlayData.colorData.channelAdditiveMask[k / 2] = color;
                        }
                        changed = true;
                    }
                }

                GUIHelper.EndVerticalPadded(2f);
                GUILayout.Space(2f);
                return changed;
            }
        }
    }



    public class TextureEditor
    {
        private Texture _texture;
        private int _channel;
        private OverlayData _overlay;
        private float origLabelWidth;
        private int origIndentLevel;


        public TextureEditor(Texture texture, int channel, OverlayData overlay)
        {
            _texture = texture;
            _channel = channel;
            _overlay = overlay;
        }

        public bool OnGUI(bool allowEdits = true)
        {
            bool changed = false;

            InitEditor();
            var newTexture = (Texture)EditorGUILayout.ObjectField("", _texture, typeof(Texture), false, GUILayout.Width(100));
            RestoreEditor();

            if (allowEdits && (newTexture != _texture))
            {
                _texture = newTexture;
                changed = true;
            }

            return changed;
        }

        private void RestoreEditor()
        {
            EditorGUI.indentLevel = origIndentLevel;
            EditorGUIUtility.labelWidth = origLabelWidth;
        }

        private void InitEditor()
        {
            origLabelWidth = EditorGUIUtility.labelWidth;
            origIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = 0;
        }

        public bool OnBlendGUI()
        {
            bool changed = false;

            InitEditor();
            var currentBlendMode = _overlay.GetOverlayBlend(_channel);
            var newBlendMode = (OverlayDataAsset.OverlayBlend)EditorGUILayout.EnumPopup(currentBlendMode, GUILayout.Width(100));
            RestoreEditor();

            if (newBlendMode != currentBlendMode)
            {
                _overlay.SetOverlayBlend(_channel, newBlendMode);
                changed = true;
            }

            return changed;

        }

        public bool OnTileGUI()
        {
            bool changed = false;
            InitEditor();
            var currentTile = _overlay.IsTextureTiled(_channel);
            var newTile = (bool) EditorGUILayout.ToggleLeft("Tile", currentTile, GUILayout.Width(100));
            RestoreEditor();
            if (currentTile != newTile)
            {
                _overlay.SetTextureTiling(_channel, newTile);
                changed = true;
            }
            return changed;
        }
    }

    public class ColorEditor
    {
        public Color color;
        public string description;

        public ColorEditor(Color color, string description)
        {
            this.color = color;
            this.description = description;
        }
    }

#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
	public class ProceduralPropertyEditor
	{
		public OverlayData.OverlayProceduralData property;
		public ProceduralPropertyDescription description;

		public ProceduralPropertyEditor(OverlayData.OverlayProceduralData prop, ProceduralPropertyDescription desc)
		{
			this.property = prop;
			this.description = desc;
			if (this.description == null)
			{
				this.description = new ProceduralPropertyDescription();
				this.description.name = this.property.name;
				this.description.type = this.property.type;
				this.description.label = this.property.name;
			}
		}

		public bool OnGUI()
		{
			bool changed = false;

			GUILayout.BeginHorizontal();

			switch (this.property.type)
			{
				case ProceduralPropertyType.Boolean:
					bool newBool = EditorGUILayout.Toggle(description.label, property.booleanValue);
					if (newBool != property.booleanValue)
					{
						property.booleanValue = newBool;
						changed = true;
					}
					break;
				case ProceduralPropertyType.Color3:
				case ProceduralPropertyType.Color4:
					Color newColor = EditorGUILayout.ColorField(description.label, property.colorValue);
					if (newColor != property.colorValue)
					{
						property.colorValue = newColor;
						changed = true;
					}
					break;
				case ProceduralPropertyType.Enum:
					int newEnum = EditorGUILayout.Popup(description.label, property.enumValue, description.enumOptions);
					if (newEnum != property.enumValue)
					{
						property.enumValue = newEnum;
						changed = true;
					}
					break;
				case ProceduralPropertyType.Float:
					float newFloat = property.floatValue;
					if (description.hasRange)
					{
						newFloat = EditorGUILayout.Slider(description.label, property.floatValue, description.minimum, description.maximum);
					}
					else
					{
						newFloat = EditorGUILayout.FloatField(description.label, property.floatValue);
					}
					if (newFloat != property.floatValue)
					{
						property.floatValue = newFloat;
						changed = true;
					}
					break;
				case ProceduralPropertyType.Texture:
					Texture2D newTexture = (Texture2D) EditorGUILayout.ObjectField(description.label, property.textureValue, typeof(Texture2D), false, GUILayout.Width(100));
					if (newTexture != property.textureValue)
					{
						property.textureValue = newTexture;
						changed = true;
					}
					break;
				
				// TODO - Should be using description.componentLabels for these
				case ProceduralPropertyType.Vector2:
					Vector2 oldVector2 = new Vector2(property.vectorValue.x, property.vectorValue.y);
					Vector2 newVector2 = EditorGUILayout.Vector2Field(description.label, oldVector2);
					if (newVector2 != oldVector2)
					{
						property.vectorValue.x = newVector2.x;
						property.vectorValue.y = newVector2.y;
						changed = true;
					}
					break;
				case ProceduralPropertyType.Vector3:
					Vector3 oldVector3 = new Vector3(property.vectorValue.x, property.vectorValue.y, property.vectorValue.z);
					Vector3 newVector3 = EditorGUILayout.Vector3Field(description.label, oldVector3);
					if (newVector3 != oldVector3)
					{
						property.vectorValue.x = newVector3.x;
						property.vectorValue.y = newVector3.y;
						property.vectorValue.z = newVector3.z;
						changed = true;
					}
					break;
				case ProceduralPropertyType.Vector4:
					Vector4 newVector4 = EditorGUILayout.Vector4Field(description.label, property.vectorValue);
					if (newVector4 != property.vectorValue)
					{
						property.vectorValue = newVector4;
						changed = true;
					}
					break;
			}

			GUILayout.EndHorizontal();

			return changed;
		}
	}
#endif

    public abstract class CharacterBaseEditor : Editor
    {
        protected readonly string[] toolbar =
        {
         "DNA", "Slots"
      };
        public static bool _AutomaticUpdates = true;
        protected Vector2 scrollPosition;
        protected string _description;
        protected string _errorMessage;
        protected bool _needsUpdate;
        protected bool _forceUpdate;
        protected bool _dnaDirty;
        protected bool _textureDirty;
        protected bool _meshDirty;
        protected Object _oldTarget;
        protected bool showBaseEditor;
        protected bool _rebuildOnLayout = false;
        protected UMAData.UMARecipe _recipe;
        //DOS made protected so childs can override
        protected static int _LastToolBar = 0;
        protected int _toolbarIndex = _LastToolBar;
        //protected float _lastScrollMax = 3000;
        protected DNAMasterEditor dnaEditor;
        protected SlotMasterEditor slotEditor;
        protected bool InitialResourcesOnlyFlag;

        public static int selectedTag = 0;

        protected bool NeedsReenable()
        {
            if (dnaEditor == null || dnaEditor.NeedsReenable())
            {
                return true;
            }

            if (dnaEditor.IsValid == false)
            {
                return true;
            }

            if (_oldTarget == target)
            {
                return false;
            }

            _oldTarget = target;
            return true;
        }

        public List<UnityEngine.Object> InspectMe = new List<UnityEngine.Object>();

        public void DoInspectors()
        {
            if (InspectMe.Count > 0)
            {
                for (int i = 0; i < InspectMe.Count; i++)
                {
                    InspectorUtlity.InspectTarget(InspectMe[i]);
                }
                InspectMe.Clear();
            }
        }


        public virtual void OnEnable()
        {
            _needsUpdate = false;
            _forceUpdate = false;
            UMATextRecipe theRecipe = target as UMATextRecipe;
            InitialResourcesOnlyFlag = theRecipe.resourcesOnly;
            EditorApplication.update += DoInspectors;
        }

        public virtual void OnDisable()
        {
            EditorApplication.update -= DoInspectors;
            if (_needsUpdate)
            {
                //if (EditorUtility.DisplayDialog("Unsaved Changes", "Save changes made to the recipe?", "Save", "Discard"))
                //{
                    DoUpdate();
                //}

                _needsUpdate = false;
                _forceUpdate = false;
            }
        }

        /// <summary>
        /// Override PreInspectorGUI in any derived editors to allow editing of new properties added to recipes.
        /// </summary>
        protected virtual bool PreInspectorGUI()
        {
            return false;
        }

        /// <summary>
        /// Override PostInspectorGUI in any derived editors to allow editing of new properties added to recipes AFTER the slots/dna section.
        /// </summary>
        protected virtual bool PostInspectorGUI()
        {
            return false;
        }

        bool? editBustedRecipe = null;

        public static string[] DefaultTags
        {
            get
            {
                return UMAEditorUtilities.GetDefaultTags();
            }
        }

        public static string DoTagSelector(string[] tagsField)
        {
            List<string> tags = new List<string>(tagsField);
            bool changed = DoTagSelector(tags);

            if (changed)
            {
                return tags[tags.Count- 1];
            }
            return "";
        }

        public static string DoTagSelector(string tagField)
        {
            List<string> tags = new List<string>();
            bool changed = DoTagSelector(tags);

            if (changed)
            {
                return tags[0];
            }
            return ""; 
        }


        public static bool DoTagSelector(List<string> tagsField)
        {
            bool changed = false;
            if (DefaultTags != null && DefaultTags.Length > 0)
            {
                if (selectedTag < 0 || selectedTag >= DefaultTags.Length)
                {
                    selectedTag = 0;
                }

                GUILayout.BeginHorizontal();
                selectedTag = EditorGUILayout.Popup(selectedTag, DefaultTags);
                string currentTag = DefaultTags[selectedTag];
                if (GUILayout.Button("Add Tag", GUILayout.Width(80)))
                {
                    if (!tagsField.Contains(currentTag))
                    {
                        tagsField.Add(currentTag);
                        changed = true;
                    }
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("No tags found");
            }

            return changed;
        }

        public override void OnInspectorGUI()
        {
            GUILayout.Label(_description);
            _AutomaticUpdates = GUILayout.Toggle(_AutomaticUpdates, "Automatic Updates");
            _forceUpdate = false;

            if (!_AutomaticUpdates)
            {
                EditorGUILayout.HelpBox("Automatic Updates are disabled. You will need to click the 'Save Recipe' button to save any changes you make.", MessageType.Warning);
                if (GUILayout.Button("Save Recipe"))
                {
                    _needsUpdate = true;
                    _forceUpdate = true;
                }
            }


           // scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUIStyle.none);

            if (target as UMATextRecipe != null)
            {
                UMATextRecipe theRecipe = target as UMATextRecipe;
#if UMA_ADDRESSABLES
                bool changed = false;
                if (!serializedObject.isEditingMultipleObjects)
                {

                    string newLabel = EditorGUILayout.TextField("Alt Addressable Label", theRecipe.label);
                    if (newLabel != theRecipe.label)
                    {
                        theRecipe.label = newLabel;
                        _needsUpdate = true;
                        _forceUpdate = true;
                    }
                    // GUIContent ToggleContent = new GUIContent("Resources Only", "When checked, This recipe will be skipped when generating Addressable Groups. This can result in duplicate assets.");
                    if (theRecipe.resourcesOnly)
                    {
                        GUILayout.Label("RESOURCES ONLY: TRUE");
                        EditorGUILayout.HelpBox("Removing the Resources Only flag will instruct UMA to include this in the addressable groups. You will need to regenerate the groups, and rebuild the addressable bundles.", MessageType.Info);
                        if (GUILayout.Button("Remove Resources Only flag"))
                        {
                            theRecipe.resourcesOnly = false;
                            DoUpdate();
                            RebuildIfNeeded();
                            /* Here: Ask to rebuild the groups using the default group builder */
                        }
                    }
                    else
                    {
                        GUILayout.Label("RESOURCES ONLY: FALSE");
                        EditorGUILayout.HelpBox("Making this Resources Only will remove this recipe, and the items contained in it, from the addressable groups. This can take a few moments. Addressable bundles will need to be rebuilt after this is toggled.", MessageType.Info);
                        if (GUILayout.Button("Make this Resources Only"))
                        {
                            theRecipe.resourcesOnly = true;
                            DoUpdate();
                            /* Here: Ask to rebuild the groups using the default group builder */
                            RebuildIfNeeded();
                        }
                    }
                } 
                EditorGUILayout.HelpBox("Checking ForceKeep will set the keep flag on the item", MessageType.Info);
                bool oldForceKeep = theRecipe.forceKeep;
                theRecipe.forceKeep = EditorGUILayout.Toggle("Force Keep", theRecipe.forceKeep);
                if (oldForceKeep != theRecipe.forceKeep)
                {
                    changed = true;
                }
                bool oldLabelLocalFiles = theRecipe.labelLocalFiles;
                EditorGUILayout.HelpBox("If you check Label Local Files, then the contents will be looked up locally, not from the index. Use this when you are substituting recipes for branding, etc.", MessageType.Info);
                theRecipe.labelLocalFiles = EditorGUILayout.Toggle("Label Local Files", theRecipe.labelLocalFiles);
                if (oldLabelLocalFiles != theRecipe.labelLocalFiles)
                {
                    changed = true;
                }

                if (changed)
                {
                    DoUpdate();
                }
#endif
            }
            if (_errorMessage != null)
            {
                EditorGUILayout.HelpBox("The Recipe Editor could not be drawn correctly because the libraries could not find some of the required Assets. The error message was...", MessageType.Warning);
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("You can either continue editing this recipe (in which case it will only contain the slots and overlays you see below) or you can fix the missing asset and come back to this recipe after (in which case it will contain everything the recipe had originally)", MessageType.Info);
                EditorGUILayout.Space();
                editBustedRecipe = editBustedRecipe == null ? false : editBustedRecipe;
                if (GUILayout.Button("Enable Editing"))
                {
                    editBustedRecipe = true;
                }
                EditorGUILayout.Space();
                //we dont want the user to edit the recipe at all in this case because if they do it will be saved incompletely
                //010212016 BUT we do need to output something else it looks like it doesn't work and you CAN still legitimately make NEW recipes even if the scene has no UMAContextBase
                //return;
                //TODO If we can find out if the recipe has a string and we DONT have an UMAContextBase we could disable editing (so the user doesn't screw up their recipes
            }

            EditorGUI.BeginDisabledGroup(editBustedRecipe == false);

            try
            {
                if (target != _oldTarget)
                {
                    _rebuildOnLayout = true;
                    _oldTarget = target;
                }

                if (_rebuildOnLayout && Event.current.type == EventType.Layout)
                {
                    Rebuild();
                }


                if (PreInspectorGUI())
                {
                    _needsUpdate = true;
                }

                if (ToolbarGUI())
                {
                    _needsUpdate = true;
                }

                if (PostInspectorGUI())
                {
                    _needsUpdate = true;
                }

                if ((_AutomaticUpdates && _needsUpdate) || _forceUpdate)
                {
                    DoUpdate();
                    _needsUpdate = false;
                    _forceUpdate = false;
                }
                else
                {
                    if (_needsUpdate)
                    {
                        var recipeBase = (UMARecipeBase)target;
                        recipeBase.Save(_recipe, UMAContextBase.Instance);
                        EditorUtility.SetDirty(target);
                    }
                }
            }
            catch (UMAResourceNotFoundException e)
            {
                _errorMessage = e.Message;
            }
            if (showBaseEditor)
            {
                base.OnInspectorGUI();
            }
            //end the busted Recipe disabled group if we had it
            EditorGUI.EndDisabledGroup();
            GUILayout.Label("** end of recipe **");
            //Rect last = GUILayoutUtility.GetLastRect();
            //float _lastMax = last.y + last.height;
            //if (_lastMax != _lastScrollMax && _lastMax > 256.0f)
            // {
            //	_lastScrollMax = _lastMax;
            //	Repaint();
            //}
            //GUILayout.EndScrollView();
        }

#if UMA_ADDRESSABLES
		private void RebuildIfNeeded()
		{
			List<Type> PluginTypes = AssetIndexerWindow.GetAddressablePlugins();

			if (EditorUtility.DisplayDialog("UMA System Request", "The Addressable groups should be recalculated after setting this. Do it now? This is recommended.", "Recalculate", "Do it later"))
			{
				//TODO: Need to support possible additions to plugin types.
				if (PluginTypes.Count == 1 && UMAEditorUtilities.UseSharedGroupConfigured())
				{

					IUMAAddressablePlugin addrplug = (IUMAAddressablePlugin)Activator.CreateInstance(PluginTypes[0]);
					UMAAddressablesSupport.Instance.GenerateAddressables(addrplug);
					Resources.UnloadUnusedAssets();
				}
				else
				{
					UMAAddressablesSupport.Instance.CleanupAddressables();
					UMAAddressablesSupport.Instance.GenerateAddressables();
					Resources.UnloadUnusedAssets();
				}
			}
		}
#endif
        protected abstract void DoUpdate();

        protected virtual void Rebuild()
        {
            _rebuildOnLayout = false;
            if (_recipe != null && dnaEditor != null)
            {
                int oldViewDNA = dnaEditor.viewDna;
                UMAData.UMARecipe oldRecipe = dnaEditor.recipe;
                dnaEditor = new DNAMasterEditor(_recipe);
                if (oldRecipe == _recipe)
                {
                    dnaEditor.viewDna = oldViewDNA;
                }
                slotEditor = new SlotMasterEditor(_recipe);
            }
        }

        //DOS Changed Toolbar to be protected virtual so children can override
        protected virtual bool ToolbarGUI()
        {
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, toolbar);
            _LastToolBar = _toolbarIndex;
            if (dnaEditor != null && slotEditor != null)
            {
                switch (_toolbarIndex)
                {
                    case 0:
                        if (!dnaEditor.IsValid)
                        {
                            return false;
                        }

                        return dnaEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
                    case 1:
                        return slotEditor.OnGUI(target.name, ref _dnaDirty, ref _textureDirty, ref _meshDirty);
                }
            }

            return false;
        }
    }
}
#endif
