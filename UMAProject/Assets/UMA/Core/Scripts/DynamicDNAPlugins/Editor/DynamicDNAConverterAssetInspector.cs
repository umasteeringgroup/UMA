using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.IMGUI.Controls;

namespace UMA.Editors
{

	[CustomEditor(typeof(DynamicDNAConverterAsset),true)]
	public class DynamicDNAConverterAssetInspector : Editor
	{

		#region FIELDS

		DynamicDNAConverterAsset _target;

		//if set will be sent to the plugins so they can draw a popup of dnaNames rather than a string field for dna selection if they wish
		private DynamicUMADnaAsset _dnaAsset;

		//the converter plugins that are available
		private List<System.Type> _availablePlugins = new List<Type>();

		//The editors for each of the converter plugins, users can make their own editors of they like so long as they descend from DynamicDNAPluginInspector
		private Dictionary<DynamicDNAPlugin, DynamicDNAPluginInspector> _pluginsEditors = new Dictionary<DynamicDNAPlugin, DynamicDNAPluginInspector>();

		// the type of converter plugin to add (set from the _availablePlugins above)
		private Type _pluginToAdd;

		private bool _helpExpanded = false;

		//if true 'view by dna name' otherwise 'view by converter type'
		private bool _view;

		//maintains a dictionary of the dna names that are expanded when in 'view by dna' mode
		private Dictionary<string, bool> _expandedDNANames = new Dictionary<string, bool>();

		private SerializedProperty _convertersListProp;
		private ReorderableList _convertersROL;

		//stores the search string (if any) when in 'view by dna' mode
		private string _DNASearchString = "";

		//styles we use 
		private GUIStyle _subHeaderStyle;
		private Texture _helpIcon;
		private GUIStyle _helpStyle;
		private GUIStyle _foldoutTipStyle;
		private GUIStyle _pluginChooserAreaStyle;
		private SearchField _dnaSearchField;
		private GUIStyle _pluginsByDNAAreaStyle;

		//GUIContent Defaults
		private string _dnaConvertersLabel = "DNA Converters";

		private string[] _viewTabsLabels = new string[] { "By Converter Type", "By DNA Name" };

		//default button sizes
		private float _addPluginBtnWidth = 50f;
		private float _deletePluginBtnWidth = 20f;

		private bool _initialized = false;

		#endregion

		#region PUBLIC PROPERTIES

		public DynamicUMADnaAsset DNAAsset
		{
			set { _dnaAsset = value; }
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
					//Style for subHeaders
					_subHeaderStyle = new GUIStyle(EditorStyles.helpBox);
					_subHeaderStyle.margin = new RectOffset(_subHeaderStyle.margin.left, _subHeaderStyle.margin.right, _subHeaderStyle.margin.top, 0);

					//Style for Tips
					_foldoutTipStyle = new GUIStyle(EditorStyles.foldout);
					_foldoutTipStyle.fontStyle = FontStyle.Bold;

					//Help Icon & style
					_helpIcon = EditorGUIUtility.FindTexture("_Help");

					_helpStyle = new GUIStyle(EditorStyles.label);
					_helpStyle.fixedHeight = _helpIcon.height + 4f;
					_helpStyle.contentOffset = new Vector2(-4f, 0f);

					//Styles for the Add Converter area
					var reorderableListDefaults = new ReorderableList.Defaults();
					_pluginChooserAreaStyle = new GUIStyle(reorderableListDefaults.boxBackground);
					_pluginChooserAreaStyle.margin = new RectOffset(4, 4, 2, 2);
					_pluginChooserAreaStyle.stretchHeight = false;
					_pluginChooserAreaStyle.padding = new RectOffset(8, 8, 4, 8);

					_pluginsByDNAAreaStyle = new GUIStyle(EditorStyles.textField);
					_pluginsByDNAAreaStyle.margin = new RectOffset(0, 0, 0, 0);
					_pluginsByDNAAreaStyle.padding = new RectOffset(4,4,4,4);

					stylesSet = true;
				}

				_initialized = stylesSet;

				_target = target as DynamicDNAConverterAsset;

				InitPlugins();
			}
			return _initialized;
		}

		private void InitPlugins()
		{
			_target.ValidatePlugins();

			_pluginsEditors.Clear();

			//initialize the editors for the existing plugins
			for (int i = 0; i < _target.PluginCount; i++)
			{
				var pluginEditor = Editor.CreateEditor(_target.GetPlugin(i)) as DynamicDNAPluginInspector;
				if (pluginEditor != null)
				{
					pluginEditor.DNAAsset = _dnaAsset;
					pluginEditor.Converter = _target;
				}
				_pluginsEditors.Add(_target.GetPlugin(i), pluginEditor);
			}
		}

		#endregion

		#region UNITY METHODS

		private void OnEnable()
		{
			_initialized = false;
		}
		
		public override void OnInspectorGUI()
		{

			serializedObject.Update();

			if (!Init())
			{
				EditorGUILayout.HelpBox("Dynamic DNA Converter Asset failed to initialize GUI. Please try selecting it again.", MessageType.Error);
				Debug.LogError("FAILED TO INITIALIZE. Bailing...");
				return;
			}

			GUIHelper.BeginVerticalPadded(3f, new Color(0.75f, 0.875f, 1f, 0.3f));

			//Draw the header and help as defined in the scope
			DrawConvertersHeader();

			//Draw the view tabs for viewing by Modifier or dna name
			DrawConvertersViewTabs();

			//Draw the GUI for each initialized plugin depending on whether the 'By Plugin' view or the 'By DNA View' was selected
			if (_view == false)
			{
				DrawConverters();
			}
			else
			{
				DrawConvertersByDNA();
			}

			GUIHelper.EndVerticalPadded(3f);
			EditorGUILayout.Space();

			serializedObject.ApplyModifiedProperties();
		}
		#endregion

		#region GUI DRAWING METHODS

		private void DrawConvertersHeader()
		{
			var scopeHeaderRect = EditorGUILayout.GetControlRect();
			scopeHeaderRect.yMin = scopeHeaderRect.yMin + 0f;//+1 if not using ROL bg for next bit
			var helpIconRect = new Rect(scopeHeaderRect.xMax - 20f, scopeHeaderRect.yMin, 20f, scopeHeaderRect.height);
			var helpIcon = new GUIContent("");
			helpIcon.image = _helpIcon;
			EditorGUI.LabelField(scopeHeaderRect, _dnaConvertersLabel.ToUpper(), EditorStyles.toolbarButton);
			_helpExpanded = GUI.Toggle(helpIconRect, _helpExpanded, helpIcon, _helpStyle);
			if (_helpExpanded)
			{
				DrawConvertersHelp();
			}
		}

		private void DrawConvertersHelp()
		{
			var info1 = "DNA Converters convert dna values into modifications to your character. Different converters apply the dna in different ways. For example a Skeleton Modifier will take a dna value and convert it into transforms that are applied to the skeleton bones. A Blendshape Modifier will convert a dna value into the power value for a blendshape.";
			var info2 = "Normally DNA Converters only do anything when the dna value is changed from its starting value, but some converters allow you to define a 'default' value (and/or a DNA name that controls the default value) and this can used to apply a modification by default. A 'Starting Pose' is a good example of this.";
			var info3 = "Converters are applied to the character from top to bottom, you can change the order by dragging the handle next to the converter entries header in the 'View By Converter Type' view.";
			var info4 = "Also in the 'View By Converter Type' view you can click the 'Cog' icon to rename or delete a converter instance. Click the 'Import' button to show the import area for the plugin, which allows you to import settings from another instance in various ways";
			var info5 = "The 'View By DNA Name' tab lists all the dna names the converters can use. Expanding a dna name shows you all the converters that use that dna name in any way.";
			EditorGUILayout.HelpBox(info1, MessageType.None);
			EditorGUILayout.HelpBox(info2, MessageType.None);
			EditorGUILayout.HelpBox(info3, MessageType.None);
			EditorGUILayout.HelpBox(info4, MessageType.None);
			EditorGUILayout.HelpBox(info5, MessageType.None);
		}

		//Draws the 'View' tabs allowing the user to switch between viewing data 'By Plugin' or 'By DNA'
		private void DrawConvertersViewTabs()
		{
			//Tabs for viewing by modifier or by dna
			var tabsRect = EditorGUILayout.GetControlRect();
			var tabsLabel = new Rect(tabsRect.xMin, tabsRect.yMin, 60f, tabsRect.height);
			var tabsButRect = new Rect(tabsLabel.xMax, tabsRect.yMin, (tabsRect.width - tabsLabel.width), tabsRect.height);

			EditorGUI.LabelField(tabsLabel, "View:", EditorStyles.toolbarButton);

			var scopeViewInt = (_view ? 1 : 0);
			EditorGUI.BeginChangeCheck();
			scopeViewInt = GUI.Toolbar(tabsButRect, scopeViewInt, _viewTabsLabels, EditorStyles.toolbarButton);
			if (EditorGUI.EndChangeCheck())
			{
				_view = scopeViewInt == 0 ? false : true;
				//refresh the used dnaNames
				_target.GetUsedDNANames(true);
			}
		}

		//Draws the plugins in 'By Converter' view
		private void DrawConverters()
		{
			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));

			if (_target.PluginCount == 0)
			{
				EditorGUILayout.HelpBox("No Converters have been added yet. Use the 'Add' tool below to add some", MessageType.Info);
			}
			_convertersListProp = serializedObject.FindProperty("_plugins");

			_convertersROL = CachedReorderableList.GetListDrawer(_convertersListProp, DrawConverterListHeaderCallback, GetConverterListEntryHeightCallback, DrawConverterListEntryCallback, DrawConverterListFooterCallback);
			_convertersROL.headerHeight = 0f;
			_convertersROL.footerHeight = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2);
			_convertersROL.DoLayoutList();

			GUIHelper.EndVerticalPadded(3);
		}

		//Draws the converters in the 'By DNA' view
		private void DrawConvertersByDNA()
		{
			var inUseNames = _target.GetUsedDNANames();
			List<string> namesToDraw;

			//if we have a dnanames asset we will loop through those names
			if (_dnaAsset != null && _dnaAsset.Names.Length > 0)
			{
				namesToDraw = new List<string>(_dnaAsset.Names);
			}
			//otherwise we will used the inUseNames from the plugins
			else
			{
				namesToDraw = inUseNames;
			}

			//Draw the output
			if (namesToDraw.Count == 0)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				if (_target.PluginCount == 0)
				{
					EditorGUILayout.HelpBox("No plugins have been added yet. Use the 'Add' tool below to add some", MessageType.Info);
				}
				else
				{
					EditorGUILayout.HelpBox("No plugins have been set up to use any dnaNames yet. Switch to the other view to add them", MessageType.Info);
				}
				GUIHelper.EndVerticalPadded(3);
			}
			else
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));

				//Draw the search field
				namesToDraw = DrawDNASearchArea(EditorGUILayout.GetControlRect(), namesToDraw);

				DynamicDNAPlugin plugin;

				for (int i = 0; i < namesToDraw.Count; i++)
				{
					if (!_expandedDNANames.ContainsKey(namesToDraw[i]))
					{
						_expandedDNANames.Add(namesToDraw[i], false);
					}
					GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
					EditorGUI.indentLevel++;
					_expandedDNANames[namesToDraw[i]] = EditorGUILayout.Foldout(_expandedDNANames[namesToDraw[i]], namesToDraw[i]);
					EditorGUI.indentLevel--;
					GUILayout.EndHorizontal();
					if (_expandedDNANames[namesToDraw[i]])
					{
						GUI.color = new Color(0.75f, 0.875f, 1f, 0.3f);
						GUILayout.BeginVertical(_pluginsByDNAAreaStyle);
						GUI.color = Color.white;

						//these would be a reorderable list in the other view so draw a reorderable list box around this whole list so it looks the same
						//(plugins are not reorderable in this view because that would sort of suggest you can have plugins output in a different order
						//depending on the dna name- which you cant)
						GUILayout.BeginVertical(_pluginChooserAreaStyle);

						for (int pi = 0; pi < _target.PluginCount; pi++)
						{
							plugin = _target.GetPlugin(pi);

							if (plugin == null)
								continue;

							//make a space like the other view
							if(pi > 0)
								GUILayout.Space(EditorGUIUtility.standardVerticalSpacing *2);

							//tell the plugin to draw its entry for this dna name, plugins might use more than one dna name so its up to their drawers to sort out what to draw
							//the general idea is that if this dna name appears anywhere in the plugin, then it should draw the relevant entry
							_pluginsEditors[plugin].OnInspectorForDNAGUI(namesToDraw[i]);
						}

						GUILayout.EndVertical();

						GUILayout.EndVertical();
					}
				}
				//TODO after we have drawn all the namesToDraw if there are any inUseNames that have not been drawn, draw those too
				GUIHelper.EndVerticalPadded(3);
			}
		}

		#endregion

		#region REORDERABLE LIST CALLBACKS

		private void DrawConverterListHeaderCallback(Rect rect)
		{
			//Hide the header unless needed
			_convertersROL.headerHeight = 0f;
		}

		private float GetConverterListEntryHeightCallback(int index)
		{
			var plugin = _target.GetPlugin(index);

			if (plugin == null)
				return 0f;

			return _pluginsEditors[plugin].GetInspectorHeight();
		}

		private void DrawConverterListEntryCallback(Rect rect, int index, bool isActive, bool isFocused)
		{
			var plugin = _target.GetPlugin(index);

			if (plugin == null)
				return;

			var prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			_pluginsEditors[plugin].DrawInspectorGUI(rect);
			
			EditorGUI.indentLevel = prevIndent;
		}

		private void DrawConverterListFooterCallback(Rect rect)
		{
			//Draw the Add Converter popup instead of the +/- buttons
			DrawAddConverterPopup(rect);
		}

		#endregion

		#region GUI UTILS

		//Draws a popup showing the available plugins for the project
		private void DrawAddConverterPopup(Rect position)
		{
			var ROLDefaults = new ReorderableList.Defaults();
			var padding = 4f;
			_availablePlugins = DynamicDNAPlugin.GetAvailablePluginTypes();

			Rect addRect = Rect.zero;

			if (position == Rect.zero)
			{
				//doesnt work in Roerderable list drawer
				GUILayout.BeginVertical(_pluginChooserAreaStyle);
				addRect = EditorGUILayout.GetControlRect();
			}
			else
			{
				addRect = position;
			}
			addRect.xMin = addRect.xMax - 190 > addRect.xMin ? addRect.xMax - 190 : addRect.xMin;
			var labelRect = new Rect(addRect.xMin + (padding * 2), addRect.yMin, addRect.width - (padding * 2), 0);
			var addPopupRect = new Rect(addRect.xMin + (padding * 2), labelRect.yMax, addRect.width - _addPluginBtnWidth - (padding * 2), EditorGUIUtility.singleLineHeight);
			var addBtnRect = new Rect(addPopupRect.xMax + padding, labelRect.yMax, _addPluginBtnWidth - (padding * 3), EditorGUIUtility.singleLineHeight);

			if (Event.current.type == EventType.Repaint)
			{
				var prevFooterFixedHeight = ROLDefaults.footerBackground.fixedHeight;
				ROLDefaults.footerBackground.fixedHeight = addRect.height;
				ROLDefaults.footerBackground.Draw(addRect, false, false, false, false);
				ROLDefaults.footerBackground.fixedHeight = prevFooterFixedHeight;
			}

			var dropdownLabel = _pluginToAdd != null ? _pluginToAdd.Name : "Add Converters...";
			//TODO this can just be a popup now we dont need to disable any options if they are already used...
			if (EditorGUI.DropdownButton(addPopupRect, new GUIContent(dropdownLabel, "Add converters of the selected type to the " + _dnaConvertersLabel + " list"), FocusType.Keyboard))
			{
				// create the menu and add items to it
				GenericMenu popupMenu = new GenericMenu();

				//add the choose entry- clears the selection
				AddMenuItemForAddConvertersPopup(popupMenu, null);

				//add the actual entries
				for (int i = 0; i < _availablePlugins.Count; i++)
					AddMenuItemForAddConvertersPopup(popupMenu, _availablePlugins[i]);

				// display the menu
				popupMenu.DropDown(addPopupRect);
			}

			EditorGUI.BeginDisabledGroup(_pluginToAdd == null);
			if (GUI.Button(addBtnRect, new GUIContent("Add", (_pluginToAdd == null ? "Choose converters to add first" : ""))))
			{
				//do it!
				_target.AddPlugin(_pluginToAdd);
				//reset the choice
				_pluginToAdd = null;
				//reInit the plugins
				InitPlugins();
			}
			EditorGUI.EndDisabledGroup();

			if (position == Rect.zero)
			{
				GUILayout.EndVertical();
			}
		}

		/// <summary>
		/// Adds a menu item to the custom popup we draw for selecting a plugin to add
		/// </summary>
		private void AddMenuItemForAddConvertersPopup(GenericMenu menu, Type pluginType)
		{
			if (pluginType == null)//the choose Plugin Entry
			{
				var cbObj = new ConverterToChoose(pluginType);
				var selected = _pluginToAdd == null;
				menu.AddItem(new GUIContent("Add Converters..."), selected, OnAddConvertersPopupItemSelected, cbObj);
			}
			else
			{
				var cbObj = new ConverterToChoose(pluginType);
				var selected = (_pluginToAdd != null && _pluginToAdd.Equals(pluginType)) ? true : false;
				menu.AddItem(new GUIContent(pluginType.Name.Replace("Plugin", "")+"s"), selected, OnAddConvertersPopupItemSelected, cbObj);
			}
		}

		/// <summary>
		/// Callback for the custom menu items in the popup we draw for selecting a plugin to add
		/// </summary>
		private void OnAddConvertersPopupItemSelected(object pluginToChoose)
		{
			_pluginToAdd = ((ConverterToChoose)pluginToChoose).converterType;
		}

		/// <summary>
		/// Draws a search area for the dna view
		/// </summary>
		/// <returns>returns a list of names with the search filter applied</returns>
		private List<string> DrawDNASearchArea(Rect position, List<string> namesList)
		{
			if (_dnaSearchField == null)
				_dnaSearchField = new UnityEditor.IMGUI.Controls.SearchField();
			_DNASearchString = _dnaSearchField.OnToolbarGUI(position, _DNASearchString);

			if (String.IsNullOrEmpty(_DNASearchString))
				return namesList;

			//loop backwards over the list so we can remove stuff without out of range shiz
			for (int i = namesList.Count - 1; i >= 0; i--)
			{
				if (namesList[i].IndexOf(_DNASearchString) == -1)
				{
					namesList.RemoveAt(i);
				}
			}

			return namesList;
		}

		#endregion

		#region SPECIAL TYPES

		/// <summary>
		/// a special type to hold the data for choosing given plugin type. Used by plugins popup chooser
		/// </summary>
		private class ConverterToChoose
		{
			public Type converterType;

			public ConverterToChoose() { }

			public ConverterToChoose(Type pt)
			{
				converterType = pt;
			}
		}

		#endregion

	}
}
