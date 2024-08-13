using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.IMGUI.Controls;

namespace UMA.Editors
{
    //This is a pretty sophisticated editor but *Dont Worry* this editor will pretty much handle any plugin that descends from
    //DynamicDNAPlugin as is. You dont have to make an inspector like this of your own.
    //There are plenty of protected virtual methods and fields you can override if you do need to add
    //specific functionality though (see SkeletonModifiersDNAConverterPluginInspector for an example)
    [CustomEditor(typeof(DynamicDNAPlugin), true)]
	public class DynamicDNAPluginInspector : Editor
	{
		#region FIELDS

		protected DynamicDNAPlugin _target;

		protected DynamicDNAConverterController _converter;

		protected DynamicUMADnaAsset _dnaAsset;

		protected bool _initialized = false;

		protected bool _isExpanded = false;

		protected Dictionary<string, bool> _isExpandedForDNA = new Dictionary<string, bool>();

		protected bool _helpExpanded = false;

		protected Dictionary<string, bool> _helpExpandedForDNA = new Dictionary<string, bool>();

		protected bool _importToolsExpanded = false;

		protected int pluginImportMethod = 0;//by default this is Add

		protected string pluginImportTip = "Choose whether to 'Add' the settings to the current list, or 'Replace' the settings with the new list, or 'Overwrite' existing settings with corresponding ones in the imported list, or overwrite any existing settings and add any missing ones.";

		protected ReorderableList _converterElementsList;
		protected SerializedProperty _converterElementsProp;

		//reorderable lists call the GetElementHeightCallback continuously, which is slow for us because we have to call 
		//EditorGUI.GetPropertyHeight to get the heights (because we dont know what fields the plugins have)
		//This is a dictionary to cache the results of the first request to speed things up (ALOT!)
		protected Dictionary<int, CachedElement> _cachedArrayElementsByIndex = new Dictionary<int, CachedElement>();

		//Tools & styles
		private Texture _importIcon;
		private GUIStyle _importStyle;
		private float _importToolsHeight = 55f;
		private GUIStyle _importToolsDropAreaStyle;
		private Texture _helpIcon;
		private GUIStyle _helpStyle;
		private Texture _toolsIcon;
		private GUIStyle _toolsStyle;
		private GUIStyle _deleteBtnStyle;
		private float _toolsWidth = 40f;
		private ReorderableList.Defaults _reorderableListDefaults;

		private GUIStyle _entryBackgroundStyle;
		private GUIStyle _pluginFoldoutStyle;
		private GUIStyle _pluginFoldoutBackground;
		private GUIStyle _converterElementsHeaderBackground;

		//Fields for searching elements in the plugins converters list
		/// <summary>
		/// the minimum list count for drawing the search field. set to -1 to permanently disable search
		/// </summary>
		private float _minCountForSearch = 10;
		private SearchField _elementSearchField;
		private string _elementSearchString = "";
		private float _elementSearchHeight;

		//Fields for renaming the plugin
		private bool _renamingPlugin;
		private string _renamingPluginTo = "";

		private int _entryIndexToDelete = -1;

		#endregion

		#region PUBLIC PROPERTIES

		public DynamicUMADnaAsset DNAAsset
		{
			get { return _dnaAsset; }
			set { _dnaAsset = value; }
		}

		public DynamicDNAConverterController Converter
		{
			get { return _converter; }
			set { _converter = value; }
		}

		protected string elementSearchString
		{
			get { return _elementSearchString; }
			set
			{
				if (_elementSearchString != value)
				{
					_cachedArrayElementsByIndex.Clear();
				}
				_elementSearchString = value;
			}
		}

		#endregion

		#region INIT

		private bool Init()
		{
			if (!_initialized)
			{
				bool stylesSet = false;

				if (EditorStyles.helpBox == null || EditorStyles.foldout == null || EditorStyles.label == null)
				{
					//Dont set any styles
				}
				else
				{
					_pluginFoldoutStyle = new GUIStyle(EditorStyles.foldout);
					_pluginFoldoutStyle.clipping = TextClipping.Clip;
					_pluginFoldoutStyle.fontStyle = FontStyle.Bold;
					//Help Icon & style
					_helpIcon = EditorGUIUtility.FindTexture("_Help");

					_helpStyle = new GUIStyle(EditorStyles.label);
					_helpStyle.fixedHeight = _helpIcon.height + 4f;
					_helpStyle.contentOffset = new Vector2(-4f, -1f);

					_importIcon = EditorGUIUtility.FindTexture("CollabPull");
					_importStyle = new GUIStyle(EditorStyles.label);
					_importStyle.fixedHeight = _importIcon.height + 4f;
					_importStyle.contentOffset = new Vector2(-4f, -0f);

					_importToolsDropAreaStyle = new GUIStyle(EditorStyles.textArea);
					_importToolsDropAreaStyle.alignment = TextAnchor.UpperCenter;
					_importToolsDropAreaStyle.padding = new RectOffset(4,4,4,4);

					//By DNA Entries Background style
					_reorderableListDefaults = new ReorderableList.Defaults();
					_pluginFoldoutBackground = new GUIStyle(EditorStyles.toolbar);//use Toolbar now because all the ROLS actually have a header
					_pluginFoldoutBackground.fixedHeight += 3f;
					_converterElementsHeaderBackground = new GUIStyle(_reorderableListDefaults.headerBackground);
					_converterElementsHeaderBackground.fixedHeight = EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 4);
					_entryBackgroundStyle = new GUIStyle(_reorderableListDefaults.boxBackground);
					_entryBackgroundStyle.margin = new RectOffset(0, 0, 0, 0);
					_entryBackgroundStyle.stretchHeight = false;
					_entryBackgroundStyle.padding = new RectOffset(6, 6, 4, 8);

					_toolsIcon = EditorGUIUtility.FindTexture("_Popup");
					_toolsStyle = new GUIStyle(EditorStyles.label);
					_toolsStyle.fixedHeight = _toolsIcon.height + 4f;
					_toolsStyle.contentOffset = new Vector2(-4f, 0f);

					_deleteBtnStyle = new GUIStyle(EditorStyles.miniButton);
					_deleteBtnStyle.alignment = TextAnchor.UpperCenter;
					_deleteBtnStyle.padding.top = 1;

					_elementSearchHeight = EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 4);

					stylesSet = true;
				}

				if (stylesSet)
				{
					_target = target as DynamicDNAPlugin;
					_initialized = true;
				}
				InitPlugin();
			}
			return _initialized;
		}

		/// <summary>
		/// Override this method if your plugin requires extra initialization
		/// </summary>
		protected virtual void InitPlugin()
		{
			//
		}

		#endregion

		#region UNITY METHODS

		protected virtual void OnEnable()
		{
			_initialized = false;
		}

		public float GetInspectorHeight()
		{
			if (!Init())
            {
                return 0f;
            }
            //the foldout height
            float height = EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2);
			//plus some space at the bottom
			height += EditorGUIUtility.standardVerticalSpacing * 2;
			//help height
			if (_helpExpanded)
			{
				height += _target.GetPluginHelpHeight;
			}
			//import tools height
			if (_importToolsExpanded && _isExpanded)
			{
				height += _importToolsHeight;
			}
			//Get the expanded height from the list drawer
			if (_isExpanded)
			{
				if (_converterElementsProp == null)
                {
                    _converterElementsProp = FindConvertersListProperty();
                }

                _converterElementsList = CachedReorderableList.GetListDrawer(_converterElementsProp, DrawElementsListHeaderCallback, GetElementsListEntryHeightCallback, DrawElementsListEntryCallback, DrawElementsListFooterCallback, ElementsListOnAddCallback, ElementsListOnRemoveCallback);
				var masterWeightProp = serializedObject.FindProperty("masterWeight");
				//always draw MasterWeight
				_converterElementsList.headerHeight = (EditorGUI.GetPropertyHeight(masterWeightProp) + (EditorGUIUtility.standardVerticalSpacing * 3));
				//add any extra height the plugin is requesting
				_converterElementsList.headerHeight += _target.GetListHeaderHeight;
				//add height for the search if needed
				if (_minCountForSearch != -1 && _converterElementsProp.arraySize >= _minCountForSearch)
				{
					_converterElementsList.headerHeight += _elementSearchHeight;
				}
				//add the footer height from the plugin- by default this is the height of the ROL +/- buttons
				_converterElementsList.footerHeight = _target.GetListFooterHeight;

				height += _converterElementsList.GetHeight();
				//plus some vertical spacing
				height += (EditorGUIUtility.standardVerticalSpacing * 2);
			}
			return height;
		}

		public void DrawInspectorGUI(Rect position)
		{
			if (!Init())
            {
                return;
            }

            serializedObject.Update();

			if (_converterElementsProp == null)
            {
                _converterElementsProp = FindConvertersListProperty();
            }

            position = DrawPluginFoldout(position, "");
			if (_isExpanded)
			{
				position = DrawPluginHelp(position);
				if (_converterElementsProp != null)
				{
					position = DrawPluginImportTools(position);
					var contentsPos = new Rect(position.xMin, position.yMin, position.width, position.height);

					_converterElementsList = CachedReorderableList.GetListDrawer(_converterElementsProp, DrawElementsListHeaderCallback, GetElementsListEntryHeightCallback, DrawElementsListEntryCallback, DrawElementsListFooterCallback, ElementsListOnAddCallback, ElementsListOnRemoveCallback);

					var masterWeightProp = serializedObject.FindProperty("masterWeight");
					//always draw MasterWeight
					_converterElementsList.headerHeight = (EditorGUI.GetPropertyHeight(masterWeightProp) + (EditorGUIUtility.standardVerticalSpacing * 3));
					//add any extra height the plugin is requesting
					_converterElementsList.headerHeight += _target.GetListHeaderHeight;
					//add height for the search if needed
					if (_minCountForSearch != -1 && _converterElementsProp.arraySize >= _minCountForSearch)
					{
						_converterElementsList.headerHeight += _elementSearchHeight;
					}
					//add the footer height from the plugin- by default this is the height of the ROL +/- buttons
					_converterElementsList.footerHeight = _target.GetListFooterHeight;

					//Dragging doesnt work if we have filtered the resilts with a search
					_converterElementsList.draggable = _elementSearchString == "";
					_converterElementsList.DoList(contentsPos);
				}
			}
			else
			{
				DrawPluginHelp(position);
			}

			HandleEntryDelete();

			serializedObject.ApplyModifiedProperties();
		}

		public override void OnInspectorGUI()
		{
			if (!Init())
            {
                return;
            }

            serializedObject.Update();

			_converterElementsProp = FindConvertersListProperty();

			DrawPluginFoldout(Rect.zero);
			if (_isExpanded)
			{
				DrawPluginHelp(Rect.zero);
				if (_converterElementsProp != null)
				{
					DrawPluginImportTools(Rect.zero);

					_converterElementsList = CachedReorderableList.GetListDrawer(_converterElementsProp, DrawElementsListHeaderCallback, GetElementsListEntryHeightCallback, DrawElementsListEntryCallback, DrawElementsListFooterCallback, ElementsListOnAddCallback, ElementsListOnRemoveCallback);

					var masterWeightProp = serializedObject.FindProperty("masterWeight");
					//always draw MasterWeight
					_converterElementsList.headerHeight = (EditorGUI.GetPropertyHeight(masterWeightProp) + (EditorGUIUtility.standardVerticalSpacing * 3));
					//add any extra height the plugin is requesting
					_converterElementsList.headerHeight += _target.GetListHeaderHeight;
					//add height for the search if needed
					if (_minCountForSearch != -1 && _converterElementsProp.arraySize >= _minCountForSearch)
					{
						_converterElementsList.headerHeight += _elementSearchHeight;
					}
					//add the footer height from the plugin- by default this is the height of the ROL +/- buttons
					_converterElementsList.footerHeight = _target.GetListFooterHeight;

					_converterElementsList.draggable = _elementSearchString == "";
					_converterElementsList.DoLayoutList();
				}
			}
			else
			{
				DrawPluginHelp(Rect.zero);
			}
			HandleEntryDelete();

			serializedObject.ApplyModifiedProperties();
		}

		public virtual void OnInspectorForDNAGUI(string dnaName)
		{
			if (!Init())
            {
                return;
            }

            serializedObject.Update();

			_converterElementsProp = FindConvertersListProperty();

			//There is no elements searching in the 'By DNA' view so clear the field
			_elementSearchString = "";

			DrawPluginFoldout(Rect.zero, dnaName);
			if (_isExpandedForDNA[dnaName])
			{
				DrawPluginHelp(Rect.zero, dnaName);
				if (_converterElementsProp != null)
				{

					//var indexesForDNA = _target.IndexesForDNA(dnaName);
					int[] indexesForDNA = new int[0];
					if (_target.IndexesForDnaNames.ContainsKey(dnaName))
                    {
                        indexesForDNA = _target.IndexesForDnaNames[dnaName].ToArray();
                    }

                    //It doesn't make sense to be drawing a reorderable list here because we are not showing the whole list
                    //so pinch the style and just draw the relevant elements
                    GUILayout.BeginVertical(_entryBackgroundStyle);

					if (indexesForDNA.Length > 0)
					{
						for (int i = 0; i < indexesForDNA.Length; i++)
						{
							var rect = EditorGUILayout.GetControlRect(false, GetElementsListEntryHeightCallback(indexesForDNA[i]));
							//strip the spacing that GetControlRect adds
							rect.yMin -= EditorGUIUtility.standardVerticalSpacing * 2;
							rect.height -= EditorGUIUtility.standardVerticalSpacing * 2;
							DrawElementsListEntryCallback(rect, indexesForDNA[i], false, false);
						}
					}
					else
					{
						EditorGUILayout.HelpBox("This plugin does not use "+dnaName+". You can make it use "+dnaName+" in the other view", MessageType.Info);
					}

					GUILayout.EndVertical();
				}
			}
			else
			{
				DrawPluginHelp(Rect.zero, dnaName);
			}

			HandleEntryDelete();

			serializedObject.ApplyModifiedProperties();
		}

		#endregion

		#region GUI DRAWING METHODS


		/// <summary>
		/// Calls GetConvertersListProperty on the target plugin to get the plugins list of converters. 
		/// If this is not finding the right thing you need to override GetConvertersListProperty in the code for your plugin 
		/// </summary>
		private SerializedProperty FindConvertersListProperty()
		{
			return _target.GetConvertersListProperty(serializedObject);
		}

		/// <summary>
		/// Draws the foldout for the plugin more like a header with a count and a toolbar showing 'Import Settings' and 'Help' icons 
		/// (uses DynamicDNAPlugin.Count / DynamicDNAPlugin.CountForDNA)
		/// </summary>
		private Rect DrawPluginFoldout(Rect position, string forDNA = "")
		{
			Rect foldoutRect = Rect.zero;
			Rect toolsRect = Rect.zero;
			if (position == Rect.zero)
			{
				GUILayout.BeginHorizontal(_pluginFoldoutBackground);
				EditorGUI.indentLevel++;
				foldoutRect = EditorGUILayout.GetControlRect();
				foldoutRect.width = (foldoutRect.width - _toolsWidth) - 4f -15f;
				toolsRect = new Rect(foldoutRect.xMax + 8f, foldoutRect.yMin + 2f, _toolsWidth, foldoutRect.height);
			}
			else
			{
				var labelBGRect = new Rect(position.xMin, (position.yMin + EditorGUIUtility.standardVerticalSpacing) -1, position.width, position.height);
				EditorGUI.LabelField(labelBGRect, "", _pluginFoldoutBackground);
				//account for the drag handle and make it one line high
				foldoutRect = new Rect(position.xMin, labelBGRect.yMin, position.width, EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2f));
				foldoutRect.xMin += 15f; //ROL drag handle is 15f wide
				foldoutRect.width -= 15f;
				foldoutRect.yMin += EditorGUIUtility.standardVerticalSpacing;
				foldoutRect.width = (foldoutRect.width - _toolsWidth) - 4f;
				toolsRect = new Rect(foldoutRect.xMax, foldoutRect.yMin + 2f, _toolsWidth, foldoutRect.height);
			}

			if (!_renamingPlugin)
			{
				if (forDNA == "")
				{
					var label = new GUIContent("[" + target.name + "] (" + _converterElementsProp.arraySize + ")");
					_pluginFoldoutStyle.fixedWidth = foldoutRect.width;
					_isExpanded = EditorGUI.Foldout(foldoutRect, _isExpanded, label, true, _pluginFoldoutStyle);
				}
				else
				{
					if (!_isExpandedForDNA.ContainsKey(forDNA))
					{
						_isExpandedForDNA.Add(forDNA, false);
					}
					var count = 0;
					if (_target.IndexesForDnaNames.ContainsKey(forDNA))
                    {
                        count = _target.IndexesForDnaNames[forDNA].Count;
                    }

                    var label = new GUIContent("[" + target.name + "] using '" + forDNA + "' (" + count + ")");
					_pluginFoldoutStyle.fixedWidth = foldoutRect.width;
					_isExpandedForDNA[forDNA] = EditorGUI.Foldout(foldoutRect, _isExpandedForDNA[forDNA], label, true, _pluginFoldoutStyle);
				}
			}
			else
			{
				foldoutRect.width -= 4f;
				GUI.SetNextControlName("RenamePluginAssetField");
				_renamingPluginTo= EditorGUI.TextField(foldoutRect, _renamingPluginTo);
				EditorGUI.FocusTextInControl("RenamePluginAssetField");

				//if the user pressed return, rename the asset
				if (Event.current.keyCode == KeyCode.Return)
				{
					_target.name = _converter.GetUniquePluginName(_renamingPluginTo, _target);
					_renamingPlugin = false;
					_renamingPluginTo = "";
					EditorUtility.SetDirty(_target);
					AssetDatabase.SaveAssets();
				}
			}

			EditorGUI.BeginDisabledGroup(_renamingPlugin);

			DrawHeaderTools(toolsRect, forDNA);

			EditorGUI.EndDisabledGroup();

			if (position == Rect.zero)
			{
				EditorGUI.indentLevel--;
				GUILayout.EndHorizontal();
				return position;
			}
			else
			{
				return new Rect(position.xMin, foldoutRect.yMax, position.width, position.height - foldoutRect.height);
			}
		}

		/// <summary>
		/// Draws the header tools (the 'Import' and 'Help' toggles and the 'Tools' popup menu)
		/// </summary>
		/// <param name="forDNA">The name of the viewed DNA name if viewing in 'By DNA' mode</param>
		private void DrawHeaderTools(Rect toolsRect, string forDNA)
		{
			var helpIcon = new GUIContent("", "Show Help");
			helpIcon.image = _helpIcon;
			var importIcon = new GUIContent("", "Show Import Settings Tools");
			importIcon.image = _importIcon;
			Rect helpRect = Rect.zero;
			Rect importRect = Rect.zero;
			importRect = new Rect(toolsRect.xMin, toolsRect.yMin, toolsRect.width / 2f, toolsRect.height);
			helpRect = new Rect(importRect.xMax, toolsRect.yMin, importRect.width, toolsRect.height);
			if (_target.PluginHelp == "")
            {
                helpIcon.tooltip = "There is no help for this plugin.";
            }

            if (forDNA == "")
			{
				EditorGUI.BeginChangeCheck();
				_importToolsExpanded = GUI.Toggle(importRect, _importToolsExpanded, importIcon, _importStyle);
				if (EditorGUI.EndChangeCheck())
				{
					if (_importToolsExpanded)
                    {
                        _isExpanded = true;
                    }
                }
				//Draw help toggle (disabled if theres no help)
				EditorGUI.BeginDisabledGroup(_target.PluginHelp == "");
				_helpExpanded = GUI.Toggle(helpRect, _helpExpanded, helpIcon, _helpStyle);
				EditorGUI.EndDisabledGroup();
			}
			else
			{
				if (!_helpExpandedForDNA.ContainsKey(forDNA))
				{
					_helpExpandedForDNA.Add(forDNA, false);
				}
				//Keep the same look to the plugin, so draw the import icon but disabled
				importIcon.tooltip = "Import tools are available in 'View By Converter Type'";
				EditorGUI.BeginDisabledGroup(true);
				_importToolsExpanded = GUI.Toggle(importRect, _importToolsExpanded, importIcon, _importStyle);
				EditorGUI.EndDisabledGroup();
				//Draw help toggle (disabled if theres no help)
				EditorGUI.BeginDisabledGroup(_target.PluginHelp == "");
				_helpExpandedForDNA[forDNA] = GUI.Toggle(helpRect, _helpExpandedForDNA[forDNA], helpIcon, _helpStyle);
				EditorGUI.EndDisabledGroup();
			}
			DrawHeaderToolsButton(new Rect(toolsRect.xMax, toolsRect.yMin, toolsRect.width / 2f, toolsRect.height));
		}

		/// <summary>
		/// Draws a description of the plugin when the 'Help' icon is clicked 
		/// (uses DynamicDNAPlugin.PluginHelp)
		/// </summary>
		private Rect DrawPluginHelp(Rect position, string forDNA = "")
		{
			if ((forDNA == "" && _helpExpanded) || (forDNA != "" && _helpExpandedForDNA[forDNA]) && _target.PluginHelp != "")
			{
				if (position == Rect.zero)
				{
					position = EditorGUILayout.GetControlRect();
				}
				var helpHeight = _target.GetPluginHelpHeight;
				var helpPos = new Rect(position.xMin, position.yMin, position.width, helpHeight);
				_target.DrawPluginHelp(helpPos);

				return new Rect(position.xMin, position.yMin + helpHeight, position.width, position.height - helpHeight);
			}
			return position;
		}
		
		/// <summary>
		/// Draws the plugins Import tools when the 'Import' button is clicked
		/// </summary>
		private Rect DrawPluginImportTools(Rect position)
		{
			if (_importToolsExpanded)
			{
				//make a drop area for importing skeletonModifiers from another DynamicDnaConverter
				Rect dropArea = Rect.zero;
				if(position == Rect.zero)
				{
					dropArea = GUILayoutUtility.GetRect(0.0f, _importToolsHeight, GUILayout.ExpandWidth(true));
					dropArea.xMin = dropArea.xMin + (EditorGUI.indentLevel * 15);
				}
				else
				{
					dropArea = new Rect (position.xMin, position.yMin, position.width, _importToolsHeight);
				}
				var dropAreaPadded = new Rect(dropArea.xMin + EditorGUIUtility.standardVerticalSpacing, 
					dropArea.yMin + EditorGUIUtility.standardVerticalSpacing, 
					dropArea.width - (EditorGUIUtility.standardVerticalSpacing * 2), 
					dropArea.height - (EditorGUIUtility.standardVerticalSpacing * 2));
				GUI.Box(dropAreaPadded, "Drag other " + target.GetType().Name + "s here to import their settings", _importToolsDropAreaStyle);//Could click to pick potentially but this might be more confusing than helpful, we'll see...
				Rect selectedAddMethodRect = dropArea;
				selectedAddMethodRect.yMin = dropArea.yMax - EditorGUIUtility.singleLineHeight - 5;
				selectedAddMethodRect.xMin = dropArea.xMin - ((EditorGUI.indentLevel * 10) - 10);
				selectedAddMethodRect.xMax = dropArea.xMax - ((EditorGUI.indentLevel * 10) + 10);
				if (_target.ImportSettingsMethods.Length > 1)
				{
					pluginImportMethod = EditorGUI.Popup(selectedAddMethodRect, "Import Method:", pluginImportMethod, _target.ImportSettingsMethods);
				}
				else
				{
					pluginImportMethod = 0;
				}

				DrawImportPluginDropArea(dropArea, pluginImportMethod);

				EditorGUILayout.Space();
				if(position == Rect.zero)
				{
					return position;
				}
				else
				{
					return new Rect(position.xMin, dropArea.yMax, position.width, position.height - dropArea.height);
				}
			}
			return position;
		}

		/// <summary>
		/// Draws the default drop area for the PluginImportTools, the user can drop another instance of the same plugin here to import its settings into this plugin
		/// </summary>
		private void DrawImportPluginDropArea(Rect dropArea, int importMethod)
		{
			Event evt = Event.current;
			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
				Event.current.Use();
				return;
			}
			if (evt.type == EventType.DragPerform)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.AcceptDrag();

					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
					//we could load all assets from path here to check if there are any nested assets that are the same type as this plugin
					if (draggedObjects[0])
					{
						ImportPluginSettings(draggedObjects[0], importMethod);
					}
				}
				Event.current.Use();
				return;
			}
		}

		/// <summary>
		/// Draws the tools button for the given plugin
		/// </summary>
		private void DrawHeaderToolsButton(Rect position)
		{
			var toolsBtn = new GUIContent("");
			toolsBtn.image = _toolsIcon;
			if (GUI.Button(position, toolsBtn, _toolsStyle))
			{
				// create the Tools menu, has options for Rename, Delete, Clear Settings, Edit Script
				GenericMenu popupMenu = new GenericMenu();
				popupMenu.AddDisabledItem(new GUIContent("Type: " + _target.GetType().Name));
				popupMenu.AddSeparator("");
				popupMenu.AddItem(new GUIContent("Rename"), false, Rename);
				popupMenu.AddSeparator("");

				popupMenu = GetHeaderToolsMenuOptions(popupMenu);

				if (_converter != null)
				{
					popupMenu.AddSeparator("");
					popupMenu.AddItem(new GUIContent("Delete"), false, Delete);
				}

				popupMenu.AddSeparator("");
				popupMenu.AddItem(new GUIContent("Edit Script"), false, EditScript);
				popupMenu.DropDown(position);
			}
		}

		#endregion

		#region GUI UTILS

		/// <summary>
		/// Creates the default HeaderToolsButton menu, has options for Expand All, Collapse All, Clear Settings
		/// Override this if you want more tools in the menu
		/// </summary>
		protected virtual GenericMenu GetHeaderToolsMenuOptions(GenericMenu popupMenu)
		{
			popupMenu.AddItem(new GUIContent("Collapse All"), false, CollapseAll);
			popupMenu.AddItem(new GUIContent("Expand All"), false, ExpandAll);
			popupMenu.AddSeparator("");
			popupMenu.AddItem(new GUIContent("Clear Settings"), false, ClearSettings);

			return popupMenu;
		}

		//The methods for the default header tools
		private void ExpandAll()
		{
			bool prevExpanded = false;
			for (int i = 0; i < _cachedArrayElementsByIndex.Count; i++)
			{
				if (_cachedArrayElementsByIndex[i].inSearch != true)
                {
                    continue;
                }

                prevExpanded = _cachedArrayElementsByIndex[i].element.isExpanded;
				_cachedArrayElementsByIndex[i].element.isExpanded = true;
				if (_cachedArrayElementsByIndex[i].element.isExpanded != prevExpanded)
                {
                    GetElementsListEntryHeightCallback(i, true);
                }
            }
			_isExpanded = true;
			serializedObject.ApplyModifiedProperties();
		}

		private void CollapseAll()
		{
			bool prevExpanded = false;
			for (int i = 0; i < _cachedArrayElementsByIndex.Count; i++)
			{
				if (_cachedArrayElementsByIndex[i].inSearch != true)
                {
                    continue;
                }

                prevExpanded = _cachedArrayElementsByIndex[i].element.isExpanded;
				_cachedArrayElementsByIndex[i].element.isExpanded = false;
				if (_cachedArrayElementsByIndex[i].element.isExpanded != prevExpanded)
                {
                    GetElementsListEntryHeightCallback(i, true);
                }
            }
		}

		private void Delete()
		{
			if (EditorUtility.DisplayDialog("Delete " + _target.name, "Really delete " + _target.name + "?", "Delete", "Cancel"))
			{
				_converter.DeletePlugin(_target);
			}
		}

		private void Rename()
		{
			_renamingPlugin = true;
			_renamingPluginTo = _target.name;
		}

		private void ClearSettings()
		{
			if (EditorUtility.DisplayDialog("Clear " + _target.name, "IMPORTANT: ConverterControllers can be shared by multiple ConverterBehaviour prefabs. Clearing " + _target.name + "'s settings will affect all the converter behaviours that use it. Really clear " + _target.name + "'s settings?", "Clear", "Cancel"))
			{
				_converterElementsProp.arraySize = 0;
				serializedObject.ApplyModifiedProperties();
			}
		}

		private void EditScript()
		{
			MonoScript script = MonoScript.FromScriptableObject(_target);
			AssetDatabase.OpenAsset(script);
			GUIUtility.ExitGUI();
		}

		//When an entry's delete button is pressed it sets the _entryIndexToDelete index
		//After the list has completed drawing this is called and if _entryIndexToDelete is not -1 the entry is deleted
		private void HandleEntryDelete()
		{
			if (_entryIndexToDelete != -1)
			{
				_target.OnRemoveEntryCallback(serializedObject, _entryIndexToDelete);
				_converterElementsProp.DeleteArrayElementAtIndex(_entryIndexToDelete);
				CacheArrayElementsByIndex(true);
				_entryIndexToDelete = -1;
			}
		}

		/// <summary>
		/// Calls DynamicDNAPlugin.ImportSettings on this plugin. This should import the settings from the given UnityEngine.Object into itself (if it is the correct type)
		/// Use / override DynamicDNAPlugin.ImportSettings to change how plugins settings are imported
		/// </summary>
		private void ImportPluginSettings(UnityEngine.Object pluginToImport, int importMethod)
		{
			_target.ImportSettings(pluginToImport, importMethod);
		}

		protected virtual void DrawElementsSearch(Rect rect)
		{
			if (_elementSearchField == null)
            {
                _elementSearchField = new UnityEditor.IMGUI.Controls.SearchField();
            }
            //var searchRect = new Rect(rect.xMin, rect.yMax - (EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing)), rect.width, (EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing)));
            elementSearchString = _elementSearchField.OnToolbarGUI(rect, elementSearchString);
		}

		/// <summary>
		/// Returns whether this element should be drawn given the current search string in the elements list.
		/// Override this if you are also overriding DrawElementSearch and need different search processing
		/// </summary>
		/// <param name="index">the index of the element in the list</param>
		/// <returns>True if the element should be drawn, false otherwise</returns>
		protected virtual bool HandleElementSearch(int index)
		{
			if (_elementSearchString == "")
            {
                return true;
            }

            CacheArrayElementsByIndex();
			if (_cachedArrayElementsByIndex[index].element.displayName.IndexOf(_elementSearchString, StringComparison.CurrentCultureIgnoreCase) > -1)
            {
                return true;
            }
            else if (_target.GetPluginEntryLabel(_cachedArrayElementsByIndex[index].element, serializedObject, index).text.IndexOf(_elementSearchString, StringComparison.CurrentCultureIgnoreCase) > -1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

		public void DNANamesPopup(Rect position, SerializedProperty property, string selected)
		{
			if (_target.DNAAsset == null)
			{
				property.stringValue = EditorGUI.TextField(position, selected);
			}
			else
			{
				int selectedIndex = -1;
				var names = GetDNANamesForPopup(selected, ref selectedIndex);
				EditorGUI.BeginChangeCheck();
				selectedIndex = EditorGUI.Popup(position, selectedIndex, names.ToArray());
				if (EditorGUI.EndChangeCheck())
				{
					if (selectedIndex != 0)
                    {
                        property.stringValue = names[selectedIndex];
                    }
                }

			}
		}

		private List<string> GetDNANamesForPopup(string selected, ref int selectedIndex)
		{
			List<string> ret = new List<string>();
			for (int i = 0; i < _target.DNAAsset.Names.Length; i++)
			{
				ret.Add(_target.DNAAsset.Names[i]);
			}
			ret.Insert(0, "Choose Name");
			selectedIndex = ret.IndexOf(selected);
			if (selectedIndex == -1)
			{
				if (!string.IsNullOrEmpty(selected))
                {
                    ret.Insert(0, selected);//This is a bit confusing it needs to show as (missing)
                }
            }
			selectedIndex = ret.IndexOf(selected);
			return ret;
		}

		#endregion

		#region REORDERABLE LIST CALLBACKS

		/// <summary>
		/// Draws the backgrounds for all the header content and calls DrawElementsListHeaderContent to draw the actual content
		/// </summary>
		/// <param name="rect"></param>
		private void DrawElementsListHeaderCallback(Rect rect)
		{
			Event current = Event.current;
			if (current.type == EventType.Repaint)
			{
				var masterWeightProp = serializedObject.FindProperty("masterWeight");
				_converterElementsHeaderBackground.fixedHeight = 0;
				float masterFoldoutHeight = EditorGUIUtility.singleLineHeight +(EditorGUIUtility.standardVerticalSpacing * 3);
				float masterContentHeight = 0f;
				if (masterWeightProp.isExpanded)
				{
					masterContentHeight = EditorGUI.GetPropertyHeight(masterWeightProp) - EditorGUIUtility.singleLineHeight;
				}
				float pluginHeaderHeight = _target.GetListHeaderHeight;
				float elementSearchHeight = 0;
				if (_minCountForSearch != -1 && _converterElementsProp.arraySize >= _minCountForSearch)
				{
					elementSearchHeight  = _elementSearchHeight;
				}
				float nextYMax = rect.yMin - 1f;

				var masterFoldoutRect = new Rect(rect.xMin - 6f, nextYMax, rect.width + 12f, masterFoldoutHeight);
				_converterElementsHeaderBackground.Draw(masterFoldoutRect, GUIContent.none, false, false, false, false);
				nextYMax = masterFoldoutRect.yMax;

				if (masterWeightProp.isExpanded)
				{
					var masterContentRect = new Rect(rect.xMin - 6f, nextYMax-1f, rect.width + 12f, masterContentHeight+1f);
					_converterElementsHeaderBackground.Draw(masterContentRect, GUIContent.none, false, false, false, false);
					nextYMax = masterContentRect.yMax;
				}

				if(pluginHeaderHeight > 0)
				{
					var pluginHeaderContentRect = new Rect(rect.xMin - 6f, nextYMax -1f, rect.width + 12f, pluginHeaderHeight +1f);
					_converterElementsHeaderBackground.Draw(pluginHeaderContentRect, GUIContent.none, false, false, false, false);
					nextYMax = pluginHeaderContentRect.yMax;
				}
				if (_minCountForSearch != -1 && _converterElementsProp.arraySize >= _minCountForSearch)
				{
					var elementSearchRect = new Rect(rect.xMin - 6f, nextYMax-1f, rect.width + 12f, elementSearchHeight+1f);
					_converterElementsHeaderBackground.Draw(elementSearchRect, GUIContent.none, false, false, false, false);
				}
			}
			var contentRect = new Rect(rect.xMin, rect.yMin + (EditorGUIUtility.standardVerticalSpacing), rect.width, rect.height - (EditorGUIUtility.standardVerticalSpacing ));
			DrawElementsListHeaderContent(contentRect);
		}

		/// <summary>
		/// Draws the default elements search bar in the elements header.
		/// Override this if you need the header to contain something else. Call the base with a rect to use the search field
		/// You can change the height of this by overriding InitPlugin and changing the _converterElementsListHeaderHeight value //TODO Can we make it so the plugin doesnt need an inspector?
		/// </summary>
		/// <param name="rect"></param>
		protected virtual void DrawElementsListHeaderContent(Rect rect)
		{
			var masterWeightProp = serializedObject.FindProperty("masterWeight");
			var masterWeightRect = new Rect(rect.xMin + 6f, rect.yMin, rect.width - 12f, EditorGUI.GetPropertyHeight(masterWeightProp) + (EditorGUIUtility.standardVerticalSpacing * 2));
			EditorGUI.PropertyField(masterWeightRect, masterWeightProp, true);
			var customContentRect = new Rect(rect.xMin, masterWeightRect.yMax + (EditorGUIUtility.standardVerticalSpacing), rect.width, rect.height - masterWeightRect.height);
			//DrawElementsListHeaderContent returns true if we should do the deafult search field
			if (_target.DrawElementsListHeaderContent(customContentRect, serializedObject))
			{
				if (_minCountForSearch != -1 && _converterElementsProp.arraySize >= _minCountForSearch)
				{
					var searchRect = new Rect(rect.xMin, (rect.yMax - EditorGUIUtility.singleLineHeight) - EditorGUIUtility.standardVerticalSpacing, rect.width, (EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing * 2f)));
					DrawElementsSearch(searchRect);
				}
			}
		}

		/// <summary>
		/// Generates _cachedArrayElementsByIndex which is a dictionary of the array elements in the targets list of converters and stores their calculated height. 
		/// This is done for drawing speed. 
		/// </summary>
		/// <param name="force">Force the cache to update</param>
		protected void CacheArrayElementsByIndex(bool force = false)
		{
			if(_cachedArrayElementsByIndex.Count == 0 || force || _cachedArrayElementsByIndex.Count != _converterElementsProp.arraySize)
			{
				_cachedArrayElementsByIndex.Clear();
				for(int i = 0; i < _converterElementsProp.arraySize; i++)
				{
					_cachedArrayElementsByIndex.Add(i, new CachedElement(_converterElementsProp.GetArrayElementAtIndex(i), -1f));
				}
			}
		}

		private float GetElementsListEntryHeightCallback(int index)
		{
			return GetElementsListEntryHeightCallback(index, false);
		}
		/// <summary>
		/// The Element Height sent to the ReoderableList drawer for the element inside at index. 
		/// Calls DynamicDNAPlugin.GetPluginEntryHeight to get the height. Results are stored in _cachedArrayElementsByIndex for speed.
		/// </summary>
		protected virtual float GetElementsListEntryHeightCallback(int index, bool recalculateHeight = false)
		{
			//Reorderable lists call this lots of times and when you have alot of items this can seriously choke things up
			//if you use EditorGUI.GetPropertyHeight- which we have to because we dont know what the plugin entries will consist of
			//So cache the heights and only recalculate them if anything changes
			CacheArrayElementsByIndex();
			if (_cachedArrayElementsByIndex[index].inSearch == null)
            {
                _cachedArrayElementsByIndex[index].inSearch = HandleElementSearch(index);
            }

            if (_cachedArrayElementsByIndex[index].inSearch == false)
            {
                return 0f;
            }

            if (_cachedArrayElementsByIndex[index].height == -1 || recalculateHeight)
			{
				var entry = _cachedArrayElementsByIndex[index].element;

				if (!entry.isExpanded)
                {
                    _cachedArrayElementsByIndex[index].height = _target.GetPluginEntryHeight(serializedObject, index, entry) + (EditorGUIUtility.standardVerticalSpacing * 3);
                }
                else
                {
                    _cachedArrayElementsByIndex[index].height = _target.GetPluginEntryHeight(serializedObject, index, entry) + (EditorGUIUtility.standardVerticalSpacing * 7);
                }
            }
			return _cachedArrayElementsByIndex[index].height;
		}

		/// <summary>
		/// Draws the elements from the plugins converter list at the given index. If you override this you will need to override GetElementHeightCallback too.
		/// Calls DynamicDNAPlugin.DrawPluginEntry to draw the entry.
		/// </summary>
		protected virtual void DrawElementsListEntryCallback(Rect rect, int index, bool isActive, bool isFocused)
		{
			CacheArrayElementsByIndex();

			if (_cachedArrayElementsByIndex[index].inSearch == false)
            {
                return;
            }

            var entry = _cachedArrayElementsByIndex[index].element;

			//dragging is disabled if any search filtering has been done but keep the list in the same position on x
			if (_elementSearchString != "")
            {
                rect.xMin += 14f;
            }

            //background for the elements header
            var labelRect = new Rect(rect.xMin, rect.yMin + 2, rect.width, rect.height);
			EditorGUI.LabelField(labelRect, "", EditorStyles.toolbar);
			
			//background for the entry if its expanded
			if (entry.isExpanded)
			{
				Event current = Event.current;
				if (current.type == EventType.Repaint)
				{
					var bgRect = new Rect(rect.xMin, rect.yMin + EditorGUIUtility.singleLineHeight + (EditorGUIUtility.standardVerticalSpacing + 2), rect.width, (rect.height - (EditorGUIUtility.singleLineHeight)) - (EditorGUIUtility.standardVerticalSpacing * 3));
					_entryBackgroundStyle.Draw(bgRect, GUIContent.none, false, false, false, false);
				}
			}

			//the actual entry- should include the foldout which will be drawn overlaping the label background
			var prect = new Rect(rect.xMin + 15f, rect.yMin + EditorGUIUtility.standardVerticalSpacing + 2, (rect.width - 15f) -8f -6f, rect.height);
			EditorGUI.BeginChangeCheck();
			entry.isExpanded = _target.DrawPluginEntry(prect, serializedObject, index, entry.isExpanded, entry);
			if(EditorGUI.EndChangeCheck())
			{
				GetElementsListEntryHeightCallback(index, true);
			}
			//draw a delete button for the entry over its header
			var deleteBtnRect = new Rect(rect.xMax -18f, rect.yMin + 4, 18f, EditorGUIUtility.singleLineHeight -2f);
			if(GUI.Button(deleteBtnRect, new GUIContent("\u0078", "Delete this entry"), _deleteBtnStyle))
			{
				_entryIndexToDelete = index;
			}
		}

		/// <summary>
		/// Called when a new element is added to the Reorderable list of the plugins converters.
		/// Calls DynamicDNAPlugin.OnAddEntryCallback *after* the element is added. 
		/// </summary>
		/// <param name="list"></param>
		protected virtual void ElementsListOnAddCallback(ReorderableList list)
		{
			list.serializedProperty.arraySize++;
			list.index = list.serializedProperty.arraySize - 1;
			_target.OnAddEntryCallback(serializedObject, list.index);
			CacheArrayElementsByIndex(true);
		}

		/// <summary>
		/// Called when a new element is removed from the Reorderable list of the plugins converters.
		/// Calls DynamicDNAPlugin.OnRemoveEntryCallback *before* the element is deleted. 
		/// DynamicDNAPlugin.OnRemoveEntryCallback should return true if deleting the element is permnitted.
		/// </summary>
		/// <param name="list"></param>
		protected virtual void ElementsListOnRemoveCallback(ReorderableList list)
		{
			_target.OnRemoveEntryCallback(serializedObject, list.index);
			list.serializedProperty.DeleteArrayElementAtIndex(list.index);
			if (list.index >= list.serializedProperty.arraySize - 1)
			{
				list.index = list.serializedProperty.arraySize - 1;
			}
			CacheArrayElementsByIndex(true);
		}

		/// <summary>
		/// Draws the default reorderable list '+/-' buttons.
		/// Override this method if you want the reorderable list to have different 'Add/Remove' tools.
		/// You can change the height of this by overriding InitPlugin and changing the _converterElementsListFooterHeight value
		/// </summary>
		/// <param name="rect">The header rect sent from the ReorderableList drawer</param>
		protected virtual void DrawElementsListFooterCallback(Rect rect)
		{
			_reorderableListDefaults.DrawFooter(rect, _converterElementsList);
		}

		#endregion

		#region SPECIAL TYPES

		protected class CachedElement
		{
			public SerializedProperty element;
			public bool isExpanded;
			public float height;
			public bool? inSearch;

			public CachedElement() { }
			public CachedElement(SerializedProperty el, float h)
			{
				element = el;
				isExpanded = el.isExpanded;
				height = h;
			}
		}

		#endregion

		#region CALL PROTECTED METHOD

		//I really want 
		//GetConvertersListProperty
		//DrawPluginHelp
		//DrawElementsListHeaderContent
		//GetPluginEntryLabel
		//GetPluginEntryHeight
		//DrawPluginEntry
		//OnAddEntryCallback
		//OnRemoveEntryCallback
		//DrawElementsListFooterContent
		//ImportSettings
		//To all be defined as protected in DynamicDNAPlugin because they should only be used by this,
		//I dont want them showing up in visualStudio or whatever when someone gets a DynamicDNAPlugin from the converters list
		//BUT they cant be defined as protected BECAUSE they need to be used by this and I dont want people to have to make custom inspectors for plugins- cos its too much work
		//SO I think the methods can be gotten via reflection here

		#endregion
	}
}
