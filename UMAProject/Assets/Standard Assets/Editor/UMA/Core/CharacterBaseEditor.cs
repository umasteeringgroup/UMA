#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;
using UMA;
using UMA.Integrations; 

namespace UMAEditor
{
    public class DNAMasterEditor
    {
        private readonly Dictionary<Type, DNASingleEditor> _dnaValues = new Dictionary<Type, DNASingleEditor>();
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
            _dnaTypeNames = new string[allDna.Length];

            for (int i = 0; i < allDna.Length; i++)
            {
                var entry = allDna[i];
                var entryType = entry.GetType();

                _dnaTypes[i] = entryType;
                _dnaTypeNames[i] = entryType.Name;
                _dnaValues[entryType] = new DNASingleEditor(entry);
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
					recipe.RemoveDna(_dnaTypes[viewDna]);
					if (viewDna >= _dnaTypes.Length - 1) viewDna--;
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
                Type dnaType = _dnaTypes[viewDna];

                if (_dnaValues[dnaType].OnGUI())
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
                    _groups.Add(groupName,  @group);
                }

                var entry = new DNAFieldEditor(fieldName, field, dna);

                 @group.Add(entry);
            }

            foreach (var group in _groups.Values)
                 @group.Sort();
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

        public bool OnGUI()
        {
            bool changed = false;
            foreach (var dnaGroup in _groups.Values)
            {
                changed |= dnaGroup.OnGUI();
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

        public bool OnGUI()
        {
            _foldout = EditorGUILayout.Foldout(_foldout, _groupName);

            if (!_foldout)
                return false;

            bool changed = false;

            GUILayout.BeginVertical(EditorStyles.textField);

            foreach (var field in _fields)
            {
                changed |= field.OnGUI();
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
        private readonly string _name;
        private readonly float _value;

        public DNAFieldEditor(string name, FieldInfo field, UMADnaBase dna)
        {
            _name = name;
            _field = field;
            _dna = dna;

            _value = (float)field.GetValue(dna);
        }

        public bool OnGUI()
        {
            float newValue = EditorGUILayout.Slider(_name, _value, 0f, 1f);
            //float newValue = EditorGUILayout.FloatField(_name, _value);

            if (newValue != _value)
            {
                _field.SetValue(_dna, newValue);
                return true;
            }

            return false;
        }

        public class Comparer : IComparer <DNAFieldEditor>
        {
            public int Compare(DNAFieldEditor x, DNAFieldEditor y)
            {
                return String.CompareOrdinal(x._name, y._name);
            }
        }
    }

	public class SharedColorsCollectionEditor
	{
		private bool _foldout = true;
		static int selectedChannelCount = 2;
		String[] names = new string[4] { "1", "2", "3", "4" };
		int[] channels = new int[4] { 1, 2, 3, 4 };
		static bool[] _ColorFoldouts = new bool[0];

		public bool OnGUI (UMAData.UMARecipe _recipe)
		{
			GUILayout.BeginHorizontal (EditorStyles.toolbarButton);
			GUILayout.Space (10);
			_foldout = EditorGUILayout.Foldout (_foldout, "Shared Colors");
			GUILayout.EndHorizontal ();

			if (_foldout) {
				bool changed = false;
				GUIHelper.BeginVerticalPadded (10, new Color (0.75f, 0.875f, 1f));

				EditorGUILayout.BeginHorizontal ();
				if (_recipe.sharedColors == null)
					_recipe.sharedColors = new OverlayColorData[0];

				if (_recipe.sharedColors.Length == 0) {
					selectedChannelCount = EditorGUILayout.IntPopup ("Channels", selectedChannelCount, names, channels);
				} else {
					selectedChannelCount = _recipe.sharedColors [0].channelMask.Length;
				}

				if (GUILayout.Button ("Add Shared Color")) {
					List<OverlayColorData> sharedColors = new List<OverlayColorData> ();
					sharedColors.AddRange (_recipe.sharedColors);
					sharedColors.Add (new OverlayColorData (selectedChannelCount));
					_recipe.sharedColors = sharedColors.ToArray ();
					changed = true;
				}

				if (GUILayout.Button ("Save Collection")) {
					changed = true;
				}

				EditorGUILayout.EndHorizontal ();


				if (_ColorFoldouts.Length != _recipe.sharedColors.Length) {
					Array.Resize<bool> (ref _ColorFoldouts, _recipe.sharedColors.Length);
				}


				for (int i = 0; i < _recipe.sharedColors.Length; i++) {
					bool del = false;
					OverlayColorData ocd = _recipe.sharedColors [i];

					GUIHelper.FoldoutBar (ref _ColorFoldouts [i], i + ": " + ocd.name, out del);
					if (del) {
						List<OverlayColorData> temp = new List<OverlayColorData> ();
						temp.AddRange (_recipe.sharedColors);
						temp.RemoveAt (i);
						_recipe.sharedColors = temp.ToArray ();
						// TODO: search the overlays and adjust the shared colors
						break;
					}
					if (_ColorFoldouts [i]) {
						if (ocd.name == null)
							ocd.name = "";


						string NewName = EditorGUILayout.TextField ("Name", ocd.name);
						if (NewName != ocd.name) {
							ocd.name = NewName;
							//changed = true;
						}

						Color NewChannelMask = EditorGUILayout.ColorField ("Color Multiplier", ocd.channelMask [0]);
						if (ocd.channelMask [0] != NewChannelMask) {
							ocd.channelMask [0] = NewChannelMask;
							changed = true;
						}

						Color NewChannelAdditiveMask = EditorGUILayout.ColorField ("Color Additive", ocd.channelAdditiveMask [0]);
						if (ocd.channelAdditiveMask [0] != NewChannelAdditiveMask) {
							ocd.channelAdditiveMask [0] = NewChannelAdditiveMask;
							changed = true;
						}

						for (int j = 1; j < ocd.channelMask.Length; j++) {
							NewChannelMask = EditorGUILayout.ColorField ("Texture " + j + "multiplier", ocd.channelMask [j]);
							if (ocd.channelMask [j] != NewChannelMask) {
								ocd.channelMask [j] = NewChannelMask;
								changed = true;
							}

							NewChannelAdditiveMask = EditorGUILayout.ColorField ("Texture " + j + " additive", ocd.channelAdditiveMask [j]);
							if (ocd.channelAdditiveMask [j] != NewChannelAdditiveMask) {
								ocd.channelAdditiveMask [j] = NewChannelAdditiveMask;
								changed = true;
							}
						}
					}
				}
				GUIHelper.EndVerticalPadded (10);
				return changed;
			}
			return false;
		}
	}


    public class SlotMasterEditor
    {
        private readonly UMAData.UMARecipe _recipe;
        private readonly List<SlotEditor> _slotEditors = new List<SlotEditor>();
		private readonly SharedColorsCollectionEditor _sharedColorsEditor = new SharedColorsCollectionEditor ();

      public SlotMasterEditor(UMAData.UMARecipe recipe)
        {
            _recipe = recipe;
			if (recipe.slotDataList == null) 
			{
				recipe.slotDataList = new SlotData[0];
			}
			for (int i = 0; i < recipe.slotDataList.Length; i++ )
			{
				var slot = recipe.slotDataList[i];

				if (slot == null)
					continue;

				_slotEditors.Add(new SlotEditor(_recipe, slot, i));
			}

			_slotEditors.Sort(SlotEditor.comparer);
			if (_slotEditors.Count > 1)
			{
				var overlays1 = _slotEditors[0].GetOverlays();
				var overlays2 = _slotEditors[1].GetOverlays();
				for (int i = 0; i < _slotEditors.Count - 2; i++ )
				{
					if (overlays1 == overlays2)
						_slotEditors[i].sharedOverlays = true;
					overlays1 = overlays2;
					overlays2 = _slotEditors[i + 2].GetOverlays();
				}
			}
		}

        public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            bool changed = false;

			// Have to be able to assign a race on a new recipe.
			RaceData newRace = (RaceData)EditorGUILayout.ObjectField("RaceData",_recipe.raceData,typeof(RaceData),false);
			if (_recipe.raceData == null) {
				GUIHelper.BeginVerticalPadded (10, new Color (0.55f, 0.25f, 0.25f));		
				GUILayout.Label ("Warning: No race data is set!");
				GUIHelper.EndVerticalPadded (10);
			}
			
			
			if (_recipe.raceData != newRace)
			{
				_recipe.SetRace(newRace);
				changed = true;
			}

			if (GUILayout.Button("Remove Nulls"))
			{
				var newList = new List<SlotData>(_recipe.slotDataList.Length);
				foreach (var slotData in _recipe.slotDataList)
				{
					if (slotData != null) newList.Add(slotData);
				}
				_recipe.slotDataList = newList.ToArray();
				changed |= true;
				_dnaDirty |= true;
				_textureDirty |= true;
				_meshDirty |= true;
			}

			if (_sharedColorsEditor.OnGUI (_recipe)) {
				changed = true;
				_textureDirty = true;
			}

			var added = (SlotDataAsset)EditorGUILayout.ObjectField("Add Slot", null, typeof(SlotDataAsset), false);

            if (added != null)
            {
				var slot = new SlotData(added);
				_recipe.MergeSlot(slot, false);
                changed |= true;
                _dnaDirty |= true;
                _textureDirty |= true;
                _meshDirty |= true;
            }

            for (int i = 0; i < _slotEditors.Count; i++)
            {
                var editor = _slotEditors[i];

                if (editor == null)
                {
                    GUILayout.Label("Empty Slot");
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

    }

    public class SlotEditor
    {
		private readonly UMAData.UMARecipe _recipe;
		private readonly SlotData _slotData;
        private readonly List<OverlayData> _overlayData = new List<OverlayData>();
        private readonly List<OverlayEditor> _overlayEditors = new List<OverlayEditor>();
        private readonly string _name;

        public bool Delete { get; private set; }

        private bool _foldout = true;
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

        public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            bool delete;
            GUIHelper.FoldoutBar(ref _foldout, _name, out delete);

            if (!_foldout)
                return false;

            Delete = delete;

            bool changed = false;

            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

			if (sharedOverlays)
			{
				EditorGUILayout.LabelField("Shared Overlays");
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
            GUIHelper.EndVerticalPadded(10);

            return changed;
        }

		public static Comparer comparer = new Comparer();
		public class Comparer : IComparer <SlotEditor>
		{
			public int Compare(SlotEditor x, SlotEditor y)
			{
				if (x._overlayData == y._overlayData)
					return 0;

				if (x._overlayData == null)
					return 1;
				if (y._overlayData == null)
					return -1;

				return x._overlayData.GetHashCode() - y._overlayData.GetHashCode();
			}
		}
	}
			
	public class OverlayEditor
	{
		private readonly UMAData.UMARecipe _recipe;
		protected readonly SlotData _slotData;
		private readonly OverlayData _overlayData;
        private  ColorEditor[] _colors;
		private  bool _sharedColors;
		private readonly TextureEditor[] _textures;
        private bool _foldout = true;

        public bool Delete { get; private set; }

        public int move;
		private static OverlayData showExtendedRangeForOverlay;

		public OverlayEditor(UMAData.UMARecipe recipe, SlotData slotData, OverlayData overlayData)
        {
			_recipe = recipe;
            _overlayData = overlayData;
            _slotData = slotData;

			_sharedColors = false;
			if (_recipe.sharedColors != null)
			{
				_sharedColors = ArrayUtility.Contains<OverlayColorData>(_recipe.sharedColors, _overlayData.colorData);
			}
			if (_sharedColors && (_overlayData.colorData.name == OverlayColorData.UNSHARED))
			{
				_sharedColors = false;
			}

			_textures = new TextureEditor[overlayData.asset.textureList.Length];
			for (int i = 0; i < overlayData.asset.textureList.Length; i++)
            {
				_textures[i] = new TextureEditor(overlayData.asset.textureList[i]);
            }

            BuildColorEditors();
        }

        private void BuildColorEditors()
        {
            _colors = new ColorEditor[_overlayData.colorData.channelMask.Length * 2];

            for (int i = 0; i < _overlayData.colorData.channelMask.Length; i++)
            {
                _colors[i * 2] = new ColorEditor(
					_overlayData.colorData.channelMask[i],
                    String.Format(i == 0
                        ? "Color multiplier"
                        : "Texture {0} multiplier", i));

                _colors[i * 2 + 1] = new ColorEditor(
					_overlayData.colorData.channelAdditiveMask[i],
                    String.Format(i == 0
                        ? "Color additive"
                        : "Texture {0} additive", i));
            }
        }

        public bool OnGUI()
        {
            bool delete;
            GUIHelper.FoldoutBar(ref _foldout, _overlayData.asset.overlayName, out move, out delete);

            if (!_foldout)
                return false;

            Delete = delete;

            GUIHelper.BeginHorizontalPadded(10, Color.white);
            GUILayout.BeginVertical();

            bool changed = OnColorGUI();

            GUILayout.BeginHorizontal();
            foreach (var texture in _textures)
            {
                changed |= texture.OnGUI();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
          
            GUIHelper.EndVerticalPadded(10);

            return changed;
        }

		public bool OnColorGUI ()
		{
			bool changed = false;
			int currentsharedcol = 0;
			string[] sharednames = new string[_recipe.sharedColors.Length];

			if (_sharedColors) {
				GUIHelper.BeginVerticalPadded (2f, new Color (0.75f, 0.875f, 1f));
				GUILayout.BeginHorizontal ();
				if (GUILayout.Toggle (true, "Use Shared Color") == false) {
					// Unshare color
					_overlayData.colorData = _overlayData.colorData.Duplicate ();
					_overlayData.colorData.name = OverlayColorData.UNSHARED;
					changed = true;
				}

				for (int i = 0; i < _recipe.sharedColors.Length; i++) {
					sharednames [i] = i + ": " + _recipe.sharedColors [i].name;
					if (_overlayData.colorData.GetHashCode () == _recipe.sharedColors [i].GetHashCode ()) {
						currentsharedcol = i;
					}
				}

				int newcol = EditorGUILayout.Popup (currentsharedcol, sharednames);
				if (newcol != currentsharedcol) {
					changed = true;
					_overlayData.colorData = _recipe.sharedColors [newcol];
				}
				GUILayout.EndHorizontal ();
				GUIHelper.EndVerticalPadded (2f);
				GUILayout.Space (2f);
				return changed;
			} 

			GUIHelper.BeginVerticalPadded (2f, new Color (0.75f, 0.875f, 1f));
			GUILayout.BeginHorizontal ();

			if (_recipe.sharedColors.Length > 0) {
				if (GUILayout.Toggle (false, "Use Shared Color")) {
					// If we got rid of resetting the name above, we could try to match the last shared color?
					// Just set to default for now.
					_overlayData.colorData = _recipe.sharedColors [0];
					changed = true;
				}
			}

			GUILayout.EndHorizontal ();

			bool showExtendedRanges = showExtendedRangeForOverlay == _overlayData;
			var newShowExtendedRanges = EditorGUILayout.Toggle ("Show Extended Ranges", showExtendedRanges);
			if (showExtendedRanges != newShowExtendedRanges) {
				if (newShowExtendedRanges) {
					showExtendedRangeForOverlay = _overlayData;
				} else {
					showExtendedRangeForOverlay = null;
				}
			}

			for (int k = 0; k < _colors.Length; k++) {
				Color color;
				if (showExtendedRanges && k % 2 == 0) {
					Vector4 colorVector = new Vector4 (_colors [k].color.r, _colors [k].color.g, _colors [k].color.b, _colors [k].color.a);
					colorVector = EditorGUILayout.Vector4Field (_colors [k].description, colorVector);
					color = new Color (colorVector.x, colorVector.y, colorVector.z, colorVector.w);
				} else {
					color = EditorGUILayout.ColorField (_colors [k].description, _colors [k].color);
				}

				if (color.r != _colors [k].color.r ||
					color.g != _colors [k].color.g ||
					color.b != _colors [k].color.b ||
					color.a != _colors [k].color.a) {
					if (k % 2 == 0) {
						_overlayData.colorData.channelMask [k / 2] = color;
					} else {
						_overlayData.colorData.channelAdditiveMask [k / 2] = color;
					}
					changed = true;
				}
			}

			GUIHelper.EndVerticalPadded (2f);
			GUILayout.Space (2f);
			return changed;
		}
	}



    public class TextureEditor
    {
        private Texture _texture;

        public TextureEditor(Texture texture)
        {
            _texture = texture;
        }

        public bool OnGUI()
        {
            bool changed = false;

            float origLabelWidth = EditorGUIUtility.labelWidth;
            int origIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = 0;
            var newTexture = (Texture)EditorGUILayout.ObjectField("", _texture, typeof(Texture), false, GUILayout.Width(100));
            EditorGUI.indentLevel = origIndentLevel;
            EditorGUIUtility.labelWidth = origLabelWidth;

            if (newTexture != _texture)
            {
                _texture = newTexture;
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

    public abstract class CharacterBaseEditor : Editor
    {
        protected readonly string[] toolbar =
        {
            "DNA", "Slots"
        };
        protected string _description;
        protected string _errorMessage;
        protected bool _needsUpdate;
        protected bool _dnaDirty;
        protected bool _textureDirty;
        protected bool _meshDirty;
        protected Object _oldTarget;
        protected bool showBaseEditor;
        protected bool _rebuildOnLayout = false;
        protected UMAData.UMARecipe _recipe;
        protected int _toolbarIndex = 0;
        protected DNAMasterEditor dnaEditor;
        protected SlotMasterEditor slotEditor;

        protected bool NeedsReenable()
        {
            if (dnaEditor == null || dnaEditor.NeedsReenable())
                return true;
            if (_oldTarget == target)
                return false;
            _oldTarget = target;
            return true;
        }

		/// <summary>
		/// Override PreInspectorGUI in any derived editors to allow editing of new properties added to recipes.
		/// </summary>
		public virtual bool PreInspectorGUI()
		{
			return false;
		}

        public override void OnInspectorGUI()
        {
            GUILayout.Label(_description);

            if (_errorMessage != null)
            {
                GUI.color = Color.red;
                GUILayout.Label(_errorMessage);

                if (_recipe != null && GUILayout.Button("Clear"))
                {
				    _errorMessage = null;
                } else
                {
                    return;
                }
            }

            try
            {
                if (target != _oldTarget)
                {
                    _rebuildOnLayout = true;
                    _oldTarget = target;
                }

                if (_rebuildOnLayout && Event.current.type == EventType.layout)
                {
                    Rebuild();
                }

				_needsUpdate = PreInspectorGUI();

                if (ToolbarGUI())
                {
                    _needsUpdate = true;
                }

                if (_needsUpdate)
                {
                    DoUpdate();
                }
            } catch (UMAResourceNotFoundException e)
            {
                _errorMessage = e.Message;
            }
            if (showBaseEditor)
            {
                base.OnInspectorGUI();
            }
        }

        protected abstract void DoUpdate();

        protected virtual void Rebuild()
        {
            _rebuildOnLayout = false;
            if (_recipe != null)
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

        private bool ToolbarGUI()
        {
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, toolbar);
            switch (_toolbarIndex)
            {
                case 0:
					if (!dnaEditor.IsValid) return false;
                    return dnaEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
                case 1:
                    return slotEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
            }

            return false;
        }
    }
}
#endif
