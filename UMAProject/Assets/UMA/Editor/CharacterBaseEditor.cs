using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UMA.Common;

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
			var newToolBarIndex = EditorGUILayout.Popup("DNA", viewDna, _dnaTypeNames);
			if (newToolBarIndex != viewDna)
			{
				viewDna = newToolBarIndex;
			}


			if (viewDna >= 0 )
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
                    _groups.Add(groupName, @group);
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
            _fields.Add (field);
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

    public class SlotMasterEditor
    {
        private readonly UMAData.UMARecipe _recipe;
        private readonly List<SlotEditor> _slots = new List<SlotEditor>();

        public SlotMasterEditor(UMAData.UMARecipe recipe)
        {
            _recipe = recipe;
            foreach (var slot in recipe.slotDataList) {

                if (slot == null)
                    continue;

                _slots.Add(new SlotEditor(slot));
            }
        }

        public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
        {
            bool changed = false;

            var added = (SlotData)EditorGUILayout.ObjectField("Add Slot", null, typeof(SlotData), false);

            if (added != null)
            {
                _slots.Add(new SlotEditor(added));
                ArrayUtility.Add(ref _recipe.slotDataList, added);
                changed = true;
				_dnaDirty = true;
				_textureDirty = true;
				_meshDirty = true;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots [i];

                if (slot == null)
                {
                    GUILayout.Label ("Empty Slot");
                    continue;
                }

                changed |= slot.OnGUI (ref _dnaDirty, ref _textureDirty, ref _meshDirty);

                if (slot.Delete)
                {
					_dnaDirty = true;
					_textureDirty = true;
					_meshDirty = true;

                    _slots.RemoveAt (i);
                    _recipe.slotDataList = _recipe.slotDataList.AllExcept(i).ToArray ();
                    i--;
                    changed = true;
                }
            }

            return changed;
        }

	}

    public class SlotEditor
    {
		private readonly SlotData _slotData;
        private readonly List<OverlayData> _overlayData = new List<OverlayData>();
        private readonly List<OverlayEditor> _overlayEditors = new List<OverlayEditor>();
        private readonly string _name;

        public bool Delete { get; private set; }

        private bool _foldout = true;

        public SlotEditor(SlotData slotData)
        {
			_slotData = slotData;
            _overlayData = slotData.GetOverlayList();

            _name = slotData.slotName;

            for (int i = 0; i < _overlayData.Count; i++)
            {
                _overlayEditors.Add(new OverlayEditor(slotData, _overlayData[i]));
            }
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

            var added = (OverlayData)EditorGUILayout.ObjectField("Add Overlay", null, typeof(OverlayData), false);

            if (added != null)
            {
				_overlayEditors.Add(new OverlayEditor(_slotData, added));
                _overlayData.Add(added);
				_dnaDirty = true;
				_textureDirty = true;
				_meshDirty = true;
				changed = true;
            }

            for (int i = 0; i < _overlayEditors.Count; i++)
            {
                var overlayEditor = _overlayEditors [i];

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
                if (overlayEditor.move > 0)
                {
                    _overlayEditors.MoveElementUpAt (i);
                    _overlayData.MoveElementUpAt(i);

                    overlayEditor.move = 0;
					_textureDirty = true;
					changed = true;
                    continue;
                }

                if (overlayEditor.move < 0 && i + 1 > 0)
                {
                    _overlayEditors.MoveElementDownAt(i);
                    _overlayData.MoveElementDownAt(i);

                    overlayEditor.move = 0;
					_textureDirty = true;
					changed = true;
                    continue;
                }
            }

            GUIHelper.EndVerticalPadded (10);

            return changed;
        }
	}

    public class OverlayEditor
    {
        private readonly OverlayData _overlayData;
		private readonly SlotData _slotData;
        private  ColorEditor[] _colors;
        private readonly TextureEditor[] _textures;

        private bool _foldout = true;

        public bool Delete { get; private set; }
        public int move;
		private Color32[] oldChannelMask;
		private Color32[] oldChannelAdditiveMask;

        public OverlayEditor(SlotData slotData, OverlayData overlayData)
        {
            _overlayData = overlayData;
			_slotData = slotData;

            _textures = new TextureEditor[overlayData.textureList.Length];
            for(int i = 0; i < overlayData.textureList.Length; i++)
            {
                _textures[i] = new TextureEditor(overlayData.textureList[i]);
            }

			BuildColorEditors();
        }

		private void BuildColorEditors()
		{
			if (_overlayData.useAdvancedMasks)
			{
				_colors = new ColorEditor[_overlayData.channelMask.Length * 2];

				for (int i = 0; i < _overlayData.channelMask.Length; i++)
				{
					_colors[i * 2] = new ColorEditor(
						_overlayData.channelMask[i],
						String.Format(i == 0
							? "Color multiplier"
							: "Texture {0} multiplier", i));

					_colors[i * 2 + 1] = new ColorEditor(
						_overlayData.channelAdditiveMask[i],
						String.Format(i == 0
							? "Color additive"
							: "Texture {0} additive", i));
				}
			}
			else
			{
				_colors = new[] 
                { 
                    new ColorEditor (_overlayData.color,"Color") 
                };
			}
		}

        public bool OnGUI()
        {
            bool delete;
            GUIHelper.FoldoutBar(ref _foldout, _overlayData.overlayName, out move, out delete);

            if (!_foldout)
                return false;

            Delete = delete;

            GUIHelper.BeginHorizontalPadded(10, Color.white);
            GUILayout.BeginVertical();

            bool changed = OnColorGUI();

            GUILayout.BeginHorizontal();
            foreach (var texture in _textures)
            {
                changed |= texture.OnGUI ();
            }
            GUILayout.EndHorizontal ();

            GUILayout.EndVertical();
          
            GUIHelper.EndVerticalPadded (10);

            return changed;
        }

        public bool OnColorGUI ()
        {
            bool changed = false;

            GUILayout.BeginVertical();

			var useAdvancedMask = EditorGUILayout.Toggle("Use Advanced Color Masks", _overlayData.useAdvancedMasks);
            if (_overlayData.useAdvancedMasks)
            {
                for (int k = 0; k < _colors.Length; k++)
                {
                    Color32 color = EditorGUILayout.ColorField(_colors[k].description,
                        _colors[k].color);

                    if (color.r != _colors[k].color.r ||
                        color.g != _colors[k].color.g ||
                        color.b != _colors[k].color.b ||
                        color.a != _colors[k].color.a)
                    {
                        if (k % 2 == 0)
                        {
                            _overlayData.channelMask[k / 2] = color;
                        }
                        else
                        {
                            _overlayData.channelAdditiveMask[k / 2] = color;
                        }
                        changed = true;
                    }
                }
            }
            else
            {
                Color32 color = EditorGUILayout.ColorField(_colors[0].description,
                        _colors[0].color);

                if (color.r != _colors[0].color.r ||
                    color.g != _colors[0].color.g ||
                    color.b != _colors[0].color.b ||
                    color.a != _colors[0].color.a)
                {
                    _overlayData.color = color;
                    changed = true;
                }
            }

			if (useAdvancedMask != _overlayData.useAdvancedMasks)
			{
				if (useAdvancedMask)
				{
					if (oldChannelMask != null)
					{
						_overlayData.channelMask = oldChannelMask;
						_overlayData.channelAdditiveMask = oldChannelAdditiveMask;
					}
					_overlayData.EnsureChannels(_slotData.GetTextureChannelCount(null));
					if (_overlayData.channelMask.Length > 0)
					{
						_overlayData.channelMask[0] = _overlayData.color;
					}
				}
				else
				{
					_overlayData.color = _overlayData.channelMask[0];
					oldChannelMask = _overlayData.channelMask;
					oldChannelAdditiveMask = _overlayData.channelAdditiveMask;
					_overlayData.color = oldChannelMask[0];
					_overlayData.channelMask = null;
					_overlayData.channelAdditiveMask = null;
				}
				BuildColorEditors();			 
			}

            GUILayout.EndVertical();

            return changed;
        }
    }

    public class TextureEditor
    {
        private Texture2D _texture;

        public TextureEditor(Texture2D texture)
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
            var newTexture = (Texture2D)EditorGUILayout.ObjectField("", _texture, typeof(Texture2D), false, GUILayout.Width(100));
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
        public Color32 color;
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
			if (dnaEditor == null || dnaEditor.NeedsReenable()) return true;
			if (_oldTarget == target) return false;
			_oldTarget = target;
			return true;
		}

        public override void OnInspectorGUI ()
        {
            GUILayout.Label (_description);

            if (_errorMessage != null)
            {
                GUI.color = Color.red;
                GUILayout.Label(_errorMessage);

                if (GUILayout.Button("Clear"))
                {
                    _errorMessage = null;
                }
                else
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

                if (ToolbarGUI())
                {
					_needsUpdate = true;
                }

                if (_needsUpdate)
                {
					DoUpdate();
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
				if (oldRecipe == _recipe) {
					dnaEditor.viewDna = oldViewDNA;
				}
				slotEditor = new SlotMasterEditor(_recipe);
			}
        }

        private bool ToolbarGUI()
        {
			if (!dnaEditor.IsValid) return false;
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, toolbar);
            switch(_toolbarIndex)
            {
                case 0:
					return dnaEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
                case 1:
					return slotEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
            }

            return false;
        }
    }
}
