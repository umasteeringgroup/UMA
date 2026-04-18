#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

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
            else if (dna is UMADnaInstance)
            {
                UMADnaInstance dnaInstance = dna as UMADnaInstance;

                var col = dnaInstance.DNAInstances;
                var groups = col.GetAllDNAGroups();
                foreach (var dnaGroup in groups)
                {
                    DNAGroupEditor group;
                    _groups.TryGetValue(dnaGroup.DNAArea, out @group);
                    if (@group == null)
                    {
                        @group = new DNAGroupEditor(dnaGroup.DNAArea);
                        _groups.Add(dnaGroup.DNAArea, @group);
                    }

                    var allDNA = col.GetDNAInstancesByGroup(dnaGroup);
                    foreach (var dnaEntry in allDNA)
                    {
                        string fieldName = ObjectNames.NicifyVariableName(dnaEntry.Name);
                        var entry = new DNAFieldEditor(fieldName, dnaEntry.Name, dnaEntry.Value, dna);
                        @group.Add(entry);
                    }
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
        //private readonly FieldInfo _field;
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
                    var dnaCollection = currentDNA as UMADnaInstance;
                    if (_value != dnaCollection.GetValue(_realName))
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
                    var dnaCollection = currentDNA as UMADnaInstance;
                    dnaCollection.SetValue(_realName, newValue);
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
}
#endif
