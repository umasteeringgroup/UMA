#define UNITY_EDITOR
#if UNITY_EDITOR

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using UMA;

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
					@group.Sort();
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
					@group.Sort();
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
				return false;

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
						changed = true;
				}
				else
				{
					if ((float)_field.GetValue(currentDNA) != _value)
						changed = true;
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
				return String.CompareOrdinal(x._name, y._name);
			}
		}
	}

	public class SharedColorsCollectionEditor
	{
		static bool _foldout = true;
		static int selectedChannelCount = 3;//DOS MODIFIED made this three so colors by default have the channels for Gloss/Metallic
		String[] names = new string[4] { "1", "2", "3", "4" };
		int[] channels = new int[4] { 1, 2, 3, 4 };
		static bool[] _ColorFoldouts = new bool[0];

		public bool OnGUI(UMAData.UMARecipe _recipe)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			_foldout = EditorGUILayout.Foldout(_foldout, "Shared Colors");
			GUILayout.EndHorizontal();

			if (_foldout)
			{
				bool changed = false;
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

				EditorGUILayout.BeginHorizontal();
				if (_recipe.sharedColors == null)
					_recipe.sharedColors = new OverlayColorData[0];

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

				if (GUILayout.Button("Save Collection"))
				{
					changed = true;
				}

				EditorGUILayout.EndHorizontal();


				if (_ColorFoldouts.Length != _recipe.sharedColors.Length)
				{
					Array.Resize<bool>(ref _ColorFoldouts, _recipe.sharedColors.Length);
				}


				for (int i = 0; i < _recipe.sharedColors.Length; i++)
				{
					bool del = false;
					OverlayColorData ocd = _recipe.sharedColors[i];

					GUIHelper.FoldoutBar(ref _ColorFoldouts[i], i + ": " + ocd.name, out del);
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
					if (_ColorFoldouts[i])
					{
						if (ocd.name == null)
							ocd.name = "";


						string NewName = EditorGUILayout.TextField("Name", ocd.name);
						if (NewName != ocd.name)
						{
							ocd.name = NewName;
							//changed = true;
						}

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
							NewChannelMask = EditorGUILayout.ColorField("Texture " + j + "multiplier", ocd.channelMask[j]);
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
					}
				}
				GUIHelper.EndVerticalPadded(10);
				return changed;
			}
			return false;
		}
	}


	public class SlotMasterEditor
	{
        // the last slot dropped. Must live between instances.
        public static string LastSlot="";
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
                    FirstSlot = _recipe.GetSlot(0);
                
                foreach (OverlayDataAsset od in DraggedOverlays)
                {
                    FirstSlot.AddOverlay(new OverlayData(od));
                }
                DraggedOverlays.Clear();
            }
        }

        //DOS Changed this to protected so childs can inherit
        protected bool DropAreaGUI(Rect dropArea)
		{
			var evt = Event.current;
			int pickedCount = 0;
			//make the box clickable so that the user can select slotData assets from the asset selection window
			//TODO if we can make this so you can click multiple slots to add them make this the only option and get rid of the object selection 'field'
			if (evt.type == EventType.MouseUp)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					Debug.Log("Show Slot Picker window");
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
					Debug.Log("Slot Picked " + tempSlotDataAsset.slotName);
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

							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (System.IO.Directory.Exists(path))
							{
								RecursiveScanFoldersForAssets(path);
							}
						}
					}
					if (DraggedSlots.Count > 0 || DraggedOverlays.Count > 0)
					{
                        AddDraggedFiles();
						return true;
					}
				}
			}
			if (pickedCount > 0)
				return true;
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
			if (UMAContext.Instance != null)
			{
				if (UMAContext.Instance.HasRace(_raceData.raceName) != null)
					return true;
            }

			AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<RaceData>(_raceData.raceName);
			if (ai != null)
			{
				return true;
			}

			string path = AssetDatabase.GetAssetPath(_raceData);
			if (UMAAssetIndexer.Instance.InAssetBundle(path))
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
					continue;

				_slotEditors.Add(new SlotEditor(_recipe, slot, i));
			}

			if (_slotEditors.Count > 1)
			{
                // Don't juggle the order - this way, they're in the order they're in the file, or dropped in.
                List<SlotEditor> sortedSlots = new List<SlotEditor>(_slotEditors);
                sortedSlots.Sort(SlotEditor.comparer);
        

                // previous code didn't work when there were only two slots
                for (int i=1;i<sortedSlots.Count;i++)
                {
                    List<OverlayData> CurrentOverlays = sortedSlots[i].GetOverlays();
                    List<OverlayData> PreviousOverlays = sortedSlots[i-1].GetOverlays();

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
					UMAContext.Instance.AddRace(_recipe.raceData);
				}
				if (GUILayout.Button("Add to Global Index (Recommended)"))
				{
					UMAAssetIndexer.Instance.EvilAddAsset(typeof(RaceData), _recipe.raceData);
				}
				GUILayout.EndHorizontal();
			}

			if (_sharedColorsEditor.OnGUI(_recipe))
            {
                changed = true;
                _textureDirty = true;
            }

            GUILayout.Space(20);
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag Slots and Overlays here. Click to Pick");
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
				if(_recipe.raceData.baseRaceRecipe != null)
				{
					//check this recipe is NOT the actual race base recipe
					if (_recipe.raceData.baseRaceRecipe.name != targetName)
					{
						//we dont want to show this if this IS the base recipe
						UMAData.UMARecipe thisBaseRecipe = _recipe.raceData.baseRaceRecipe.GetCachedRecipe(UMAContext.Instance);
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
                    if (slotData != null) newList.Add(slotData);
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
					continue;
				bool found = false;
				for (int ri = 0; ri < recipeSlots.Length; ri++)
				{
					if (recipeSlots[ri] == null)
						continue;
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
    }

	public class SlotEditor
	{
		private readonly UMAData.UMARecipe _recipe;
		private readonly SlotData _slotData;
		private readonly List<OverlayData> _overlayData = new List<OverlayData>();
		private readonly List<OverlayEditor> _overlayEditors = new List<OverlayEditor>();
		private readonly string _name;

        public SlotData Slot { get { return _slotData; } }

		public bool Delete { get; private set; }

        public bool FoldOut
        {
            get
            {
                if (! SlotMasterEditor.OpenSlots.ContainsKey(_slotData.slotName))
                   SlotMasterEditor.OpenSlots.Add(_slotData.slotName, true);
                return SlotMasterEditor.OpenSlots[_slotData.slotName];
            }
            set
            {
                if (!SlotMasterEditor.OpenSlots.ContainsKey(_slotData.slotName))
                    SlotMasterEditor.OpenSlots.Add(_slotData.slotName, true);
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
			if (UMAContext.Instance != null)
			{
				if (UMAContext.Instance.HasSlot(_slotData.asset.slotName))
				{
					return true;
				}
			}

			AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<SlotDataAsset>(_slotData.asset.slotName);
			if (ai != null)
			{
				return true;
			}

			string path = AssetDatabase.GetAssetPath(_slotData.asset);
			if (UMAAssetIndexer.Instance.InAssetBundle(path))
			{
				return true;
			}

			return false;
		}

		public bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
		{
			bool delete;
            bool _foldOut = FoldOut;

			GUIHelper.FoldoutBar(ref _foldOut, _name + "      (" + _slotData.asset.name + ")", out delete);

            FoldOut = _foldOut;

            // Set this before exiting.
            Delete = delete;
             
			if (!FoldOut)
				return false;
			

            bool changed = false;

			GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

			if (!InIndex(_slotData))
			{
				EditorGUILayout.HelpBox("Slot "+_slotData.asset.name+" is not indexed!", MessageType.Error);

				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Add to Scene Only"))
				{
					UMAContext.Instance.AddSlotAsset(_slotData.asset);
				}
				if (GUILayout.Button("Add to Global Index (Recommended)"))
				{
					UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset),_slotData.asset);
				}
				GUILayout.EndHorizontal();
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
        public static NameSorter sorter = new NameSorter();
        public class NameSorter : IComparer<SlotEditor>
        {
            public int Compare(SlotEditor x, SlotEditor y)
            {
                return String.Compare(x._slotData.slotName, y._slotData.slotName);
            }
        }
        public static Comparer comparer = new Comparer();
		public class Comparer : IComparer<SlotEditor>
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
		public static Dictionary<string, bool> OverlayExpanded = new Dictionary<string, bool>();		
		private readonly UMAData.UMARecipe _recipe;
		protected readonly SlotData _slotData;
		private readonly OverlayData _overlayData;
		private readonly TextureEditor[] _textures;
		private ColorEditor[] _colors;
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
				return;
			OverlayExpanded.Add(overlayName, true);
		}

		public OverlayEditor(UMAData.UMARecipe recipe, SlotData slotData, OverlayData overlayData)
		{
			_recipe = recipe;
			_overlayData = overlayData;
			_slotData = slotData;
			EnsureEntry((overlayData.overlayName));

			// Sanity check the colors
			if (_recipe.sharedColors == null)
				_recipe.sharedColors = new OverlayColorData[0];
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
				_textures[i] = new TextureEditor(overlayData.textureArray[i]);
			}

#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
			if (overlayData.isProcedural)
			{
				ProceduralMaterial material = _overlayData.asset.material.material as ProceduralMaterial;

				_descriptions = material.GetProceduralPropertyDescriptions();
				_properties = new ProceduralPropertyEditor[overlayData.proceduralData.Length];
				for (int i = 0; i < overlayData.proceduralData.Length; i++)
				{
					ProceduralPropertyDescription description = null;
					for (int j = 0; j < _descriptions.Length; j++)
					{
						if (_descriptions[j].name == overlayData.proceduralData[i].name)
						{
							description = _descriptions[j];
							break;
						}
					}

					_properties[i] = new ProceduralPropertyEditor(overlayData.proceduralData[i], description);
				}
			}
			else
			{
				_properties = null;
			}
            #endif

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

		private bool InIndex(OverlayData _overlayData)
		{
			if (UMAContext.Instance != null)
			{
				if (UMAContext.Instance.HasOverlay(_overlayData.overlayName))
				{
					return true;
				}
			}

			AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<OverlayDataAsset>(_overlayData.asset.overlayName);
			if (ai != null)
			{
				return true;
			}

			string path = AssetDatabase.GetAssetPath(_overlayData.asset);
			if (UMAAssetIndexer.Instance.InAssetBundle(path))
			{
				return true;
			}

			return false;
		}

		public bool OnGUI()
		{
			bool delete;

			_foldout = OverlayExpanded[_overlayData.overlayName];

			GUIHelper.FoldoutBar(ref _foldout, _overlayData.asset.overlayName + "("+_overlayData.asset.material.name+")", out move, out delete);

			OverlayExpanded[_overlayData.overlayName] = _foldout;

			if (!_foldout)
				return false;

			Delete = delete;


			GUIHelper.BeginHorizontalPadded(10, Color.white);
			GUILayout.BeginVertical();



            if (!InIndex(_overlayData))
            {
                EditorGUILayout.HelpBox("Overlay " + _overlayData.asset.name + " is not indexed!", MessageType.Error);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add to Scene Only"))
                {
                    UMAContext.Instance.AddOverlayAsset(_overlayData.asset);

                }
                if (GUILayout.Button("Add to Global Index"))
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(OverlayDataAsset), _overlayData.asset);
                }
                GUILayout.EndHorizontal();
            }

            if ((_overlayData.asset.material.IsProcedural() == false) && (_overlayData.asset.material != _slotData.asset.material))
            {
                if (_overlayData.asset.material.channels.Length == _slotData.asset.material.channels.Length)
                {
                    EditorGUILayout.HelpBox("Material " + _overlayData.asset.material.name + " does not match slot material: " + _slotData.asset.material.name, MessageType.Error);
                    if (GUILayout.Button("Copy Slot Material to Overlay"))
                    {
                        _overlayData.asset.material = _slotData.asset.material;
                        EditorUtility.SetDirty(_overlayData.asset);
                        AssetDatabase.SaveAssets();
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

            // Edit the colors
            bool changed = OnColorGUI();

            // Edit the rect
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rect");
            Rect Save = _overlayData.rect;
            _overlayData.rect = EditorGUILayout.RectField(_overlayData.rect);
            if (Save.x != _overlayData.rect.x || Save.y != _overlayData.rect.y || Save.width != _overlayData.rect.width || Save.height != _overlayData.rect.height)
            {
                changed = true;
            }
            GUILayout.EndHorizontal();

#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
			// Edit the procedural properties
			if (_overlayData.isProcedural)
			{
				GUILayout.BeginVertical();
				GUILayout.Label("Procedural Settings");
				EditorGUI.indentLevel++;

				List<string> propertyList = new List<string>(_descriptions.Length);
				foreach (ProceduralPropertyDescription description in _descriptions)
				{
					// "Internal" substance properties begin with a '$'
					if (description.label.StartsWith("$"))
						continue;
					
					propertyList.Add(description.label);

					// Only collect propoerties that aren't already set
					for (int i = 0; i < _overlayData.proceduralData.Length; i++)
					{
						if (_overlayData.proceduralData[i].name == description.name)
						{
							propertyList.Remove(description.label);
							break;
						}
					}
				}
				string[] propertyArray = propertyList.ToArray();

				GUILayout.BeginHorizontal();
				_selectedProperty = EditorGUILayout.Popup(_selectedProperty, propertyArray);

				EditorGUI.BeginDisabledGroup(_selectedProperty >= propertyArray.Length);
				if (GUILayout.Button("Add", EditorStyles.miniButton, GUILayout.Width(40)))
				{
					string propertyLabel = propertyArray[_selectedProperty];
					ProceduralPropertyDescription description = null;
					for (int i = 0; i < _descriptions.Length; i++)
					{
						if (_descriptions[i].label == propertyLabel)
						{
							description = _descriptions[i];
							break;
						}
					}

					if (description != null)
					{
						OverlayData.OverlayProceduralData newProperty = new OverlayData.OverlayProceduralData();
						newProperty.name = description.name;
						newProperty.type = description.type;

						ArrayUtility.Add(ref _overlayData.proceduralData, newProperty);
						ArrayUtility.Add(ref _properties, new ProceduralPropertyEditor(newProperty, description));

						_selectedProperty = 0;
						changed = true;
					}
				}
				EditorGUI.EndDisabledGroup();

				GUILayout.EndHorizontal();

				foreach (var property in _properties)
				{
					changed |= property.OnGUI();
				}

				EditorGUI.indentLevel--;
				GUILayout.EndVertical();
			}
            #endif      

			// Edit the textures
			GUILayout.Label("Textures");
			GUILayout.BeginHorizontal();
			foreach (var texture in _textures)
			{
				changed |= texture.OnGUI(!_overlayData.isProcedural);
			}
			GUILayout.EndHorizontal();

			GUILayout.EndVertical();

			GUIHelper.EndVerticalPadded(10);

			return changed;
		}

		public bool OnColorGUI()
		{
			bool changed = false;
			int currentsharedcol = 0;
			string[] sharednames = new string[_recipe.sharedColors.Length];
			
			//DOS 13012016 if we also check here that _recipe.sharedColors still contains 
			//the desired ocd then we can save the collection when colors are deleted
			if (_overlayData.colorData.IsASharedColor && _recipe.HasSharedColor(_overlayData.colorData))
			{
				GUIHelper.BeginVerticalPadded(2f, new Color(0.75f, 0.875f, 1f));
				GUILayout.BeginHorizontal();

				if (GUILayout.Toggle(true, "Use Shared Color") == false)
				{
					// Unshare color
					_overlayData.colorData = _overlayData.colorData.Duplicate();
					_overlayData.colorData.name = OverlayColorData.UNSHARED;
					changed = true;
				}

				for (int i = 0; i < _recipe.sharedColors.Length; i++)
				{
					sharednames[i] = i + ": " + _recipe.sharedColors[i].name;
					if (_overlayData.colorData.GetHashCode() == _recipe.sharedColors[i].GetHashCode())
					{
						currentsharedcol = i;
					}
				}

				int newcol = EditorGUILayout.Popup(currentsharedcol, sharednames);
				if (newcol != currentsharedcol)
				{
					changed = true;
					_overlayData.colorData = _recipe.sharedColors[newcol];
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

		public TextureEditor(Texture texture)
		{
			_texture = texture;
		}

		public bool OnGUI(bool allowEdits = true)
		{
			bool changed = false;

			float origLabelWidth = EditorGUIUtility.labelWidth;
			int origIndentLevel = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;
			EditorGUIUtility.labelWidth = 0;
			var newTexture = (Texture)EditorGUILayout.ObjectField("", _texture, typeof(Texture), false, GUILayout.Width(100));
			EditorGUI.indentLevel = origIndentLevel;
			EditorGUIUtility.labelWidth = origLabelWidth;

			if (allowEdits && (newTexture != _texture))
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

        public virtual void OnEnable()
        {
            _needsUpdate = false;
            _forceUpdate = false;
        }

        public virtual void OnDisable()
        {
            if (_needsUpdate)
            {
                if (EditorUtility.DisplayDialog("Unsaved Changes", "Save changes made to the recipe?", "Save", "Discard"))
                    DoUpdate();
                
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

		public override void OnInspectorGUI()
		{
			GUILayout.Label(_description);
      _AutomaticUpdates = GUILayout.Toggle(_AutomaticUpdates, "Automatic Updates");
      _forceUpdate = false;

      if (!_AutomaticUpdates)
      {
          if(GUILayout.Button("Save Recipe"))
          {
              _needsUpdate = true;
              _forceUpdate = true;
          }
      }
			scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUIStyle.none , GUILayout.MinHeight(600), GUILayout.MaxHeight(3000));

			if (_errorMessage != null)
			{
				EditorGUILayout.HelpBox("The Recipe Editor could not be drawn correctly because the libraries could not find some of the required Assets. The error message was...", MessageType.Warning);
				EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
				EditorGUILayout.Space();
				EditorGUILayout.HelpBox("You can either continue editing this recipe (in which case it will only contain the slots and overlays you see below) or you can fix the missing asset and come back to this recipe after (in which case it will contain everything the recipe had originally)", MessageType.Info);
				EditorGUILayout.Space();
				editBustedRecipe = editBustedRecipe == null ? false : editBustedRecipe;
				if(GUILayout.Button("Enable Editing"))
				{
					editBustedRecipe = true;
				}
				EditorGUILayout.Space();
				//we dont want the user to edit the recipe at all in this case because if they do it will be saved incompletely
				//010212016 BUT we do need to output something else it looks like it doesn't work and you CAN still legitimately make NEW recipes even if the scene has no UMAContext
				//return;
				//TODO If we can find out if the recipe has a string and we DONT have an UMAContext we could disable editing (so the user doesn't screw up their recipes
			}
			EditorGUI.BeginDisabledGroup((editBustedRecipe == false ? true : false));

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
            GUILayout.EndScrollView();
		}

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
			switch (_toolbarIndex)
			{
				case 0:
					if (!dnaEditor.IsValid) return false;
					return dnaEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
				case 1:
					return slotEditor.OnGUI(target.name, ref _dnaDirty, ref _textureDirty, ref _meshDirty);
			}

			return false;
		}
	}
}
#endif
