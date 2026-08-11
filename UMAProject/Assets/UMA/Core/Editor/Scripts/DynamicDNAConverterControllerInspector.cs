using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.IMGUI.Controls;
using UnityEngine.Events;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Editors
{

    [CustomEditor(typeof(DynamicDNAConverterController),true)]
	public class DynamicDNAConverterControllerInspector : Editor
	{
		public static UnityEngine.Object folder = null;
		public static string folderPath = UMAPathUtility.ProjectDataRoot;

        [MenuItem("Assets/Create/UMA/DNA/Legacy/Dynamic DNA Converter Controller")]
		public static void CreateDynamicDNAConverterController()
		{
			DynamicDNAConverterController.CreateDynamicDNAConverterControllerAsset();
		}

		#region FIELDS

		private static DynamicDNAConverterControllerInspector _livePopupEditor;

		public static UnityEvent OnLivePopupEditorChange = new UnityEvent();

		DynamicDNAConverterController _target;

		//if set will be sent to the plugins so they can draw a popup of dnaNames rather than a string field for dna selection if they wish
		private DynamicUMADnaAsset _dnaAsset;

		//the converter plugins that are available
		private List<System.Type> _availablePlugins = new List<Type>();

		//The editors for each of the converter plugins, users can make their own editors of they like so long as they descend from DynamicDNAPluginInspector
		private Dictionary<DynamicDNAPlugin, DynamicDNAPluginInspector> _pluginsEditors = new Dictionary<DynamicDNAPlugin, DynamicDNAPluginInspector>();

		// the type of converter plugin to add (set from the _availablePlugins above)
		private Type _pluginToAdd;

		private bool _dnaAssetExpanded = true;

		private bool _dnaAssetHelpExpanded = false;

		private bool _convertersExpanded = true;

		private bool _convertersHelpExpanded = false;

		private bool _overallModifiersExpanded = true;

		private bool _overallModifiersHelpExpanded = false;

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

		private bool _initialized = false;

		// Cache for found DNA assets by name to avoid repeated AssetDatabase searches
		private readonly Dictionary<string, DNA> _foundDnaCache = new Dictionary<string, DNA>();

		private string[] _help = new string[]
		{
		"DNA Converters convert dna values into modifications to your character. Different converters apply the dna in different ways. For example a Skeleton DNA Converter will take a dna value and convert it into transforms that are applied to the skeleton bones. A Blendshape DNA Converter will convert a dna value into the power value for a blendshape.",
		"Normally DNA Converters only do anything when the dna value is changed from its starting value, but some converters allow you to define a 'Starting' value and this can used to apply a modification by default. A 'Starting Pose' is a good example of this.",
		"Converters are applied to the character from top to bottom, you can change the order by dragging the handle next to the converter entries header in the 'View By Converter Type' view.",
		"Also in the 'View By Converter Type' view you can click the 'Cog' icon to rename or delete a converter instance. Click the 'Import' button to show the import area for the plugin, which allows you to import settings from another instance in various ways",
		"The 'View By DNA Name' tab lists all the dna names the converters can use. Expanding a dna name shows you all the converters that use that dna name in any way."
		};

		private bool changed = false;

		#endregion

		#region PUBLIC PROPERTIES

		public DynamicUMADnaAsset DNAAsset
		{
			set { _dnaAsset = value; }
		}

		public static DynamicDNAConverterControllerInspector livePopupEditor
		{
			get { return _livePopupEditor; }
		}

		public static bool livePopupEditorChanged
		{
			get
			{
				if (_livePopupEditor != null)
                {
                    return _livePopupEditor.changed;
                }
                else
                {
                    return false;
                }
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

				_target = target as DynamicDNAConverterController;

				_dnaAsset = _target.DNAAsset;

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

			var displayValueProp = serializedObject.FindProperty("_displayValue");

			EditorGUILayout.PropertyField(displayValueProp);

			EditorGUILayout.Space();

			DrawDNAAssetField();

			//Draw the header and help as defined in the scope
			var controllerHeaderRect = EditorGUILayout.GetControlRect();
			DrawControllersHeader(controllerHeaderRect, _help, ref _convertersExpanded, ref _convertersHelpExpanded);

			if (_convertersExpanded)
			{
				//Draw the view tabs for viewing by Modifier or dna name
				DrawControllersViewTabs();

				//Draw the GUI for each initialized plugin depending on whether the 'By Plugin' view or the 'By DNA View' was selected
				EditorGUI.BeginChangeCheck();
				if (_view == false)
				{
					DrawConverters();
				}
				else
				{
					DrawConvertersByDNA();
				}
				if (EditorGUI.EndChangeCheck())
				{
					changed = true;
					if (_livePopupEditor != null && _livePopupEditor == this)
                    {
                        OnLivePopupEditorChange.Invoke();
                    }
                }
				else
                {
                    changed = false;
                }
            }

			EditorGUILayout.Space();

			DrawOverallModifiers();

			serializedObject.ApplyModifiedProperties();
		}
		#endregion

		#region GUI DRAWING METHODS

		private void DrawDNAAssetField()
		{
			var dnaAssetProp = serializedObject.FindProperty("_dnaAsset");
			var dnaAssetFoldoutRect = EditorGUILayout.GetControlRect();
			dnaAssetFoldoutRect.height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			var dnaAssetLabel = EditorGUI.BeginProperty(dnaAssetFoldoutRect, new GUIContent(dnaAssetProp.displayName), dnaAssetProp);

			dnaAssetLabel.text = dnaAssetLabel.text.ToUpper();
			GUIHelper.ToolbarStyleFoldout(dnaAssetFoldoutRect, dnaAssetLabel.text.ToUpper(), new string[] { dnaAssetLabel.tooltip }, ref _dnaAssetExpanded, ref _dnaAssetHelpExpanded);

			if (_dnaAssetExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				GUILayout.Space(5);
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(dnaAssetProp);
				if (EditorGUI.EndChangeCheck())
				{
					if (dnaAssetProp.objectReferenceValue != null)
                    {
                        _dnaAsset = dnaAssetProp.objectReferenceValue as DynamicUMADnaAsset;
                    }
                    else
                    {
                        _dnaAsset = null;
                    }
                    //TODO in the ConverterBehaviour editor we cleared the DNA on the avatar.umaData (if we are in play mode and inspecting using customizer)
                    //we could probably do with doing the same here
                    /*
					 //force the Avatar to update its dna and dnaconverter dictionaries
						umaData.umaRecipe.ClearDna();
						umaData.umaRecipe.ClearDNAConverters();
					*/
                }
				GUIHelper.EndVerticalPadded(3);
			}

			EditorGUILayout.Space();
		}

		private void DrawControllersHeader(Rect rect, string[] help, ref bool _isExpanded, ref bool _helpExpanded)
		{
			//GUIHelper.ToolbarStyleHeader(rect, new GUIContent(_dnaConvertersLabel.ToUpper()), _help, ref _helpExpanded);
			GUIHelper.ToolbarStyleFoldout(rect, new GUIContent(_dnaConvertersLabel.ToUpper()), _help, ref _isExpanded, ref _helpExpanded);
			//_isExpanded = true;
		}

		private void DrawHelp(string[] help)
		{
			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			for(int i = 0; i < help.Length; i++)
			{
				EditorGUILayout.HelpBox(help[i], MessageType.None);
			}
			GUIHelper.EndVerticalPadded(3);
		}

		private void ConvertDNA(string dnaName, string folderPath)
		{
			// Editor-only creation of DNA assets by name within the specified folder, with caching
			if (string.IsNullOrEmpty(dnaName) || string.IsNullOrEmpty(folderPath)) return;
			// Check if DNA already exists to avoid duplicates
			if (ConvertedDNAExists(dnaName, folderPath))
			{
				// DNA already exists, delete and re-convert
				AssetDatabase.DeleteAsset(System.IO.Path.Combine(folderPath, dnaName + ".asset")); 
				// Clear cache so it is recreated
				if (_foundDnaCache.ContainsKey(dnaName)) _foundDnaCache.Remove(dnaName);
             }
             try
             {
                 // Create a new DNA asset
                 DNA newDna = ScriptableObject.CreateInstance<DNA>();
                 newDna.name = dnaName;
                 // Ensure the folder path exists
                 if (!AssetDatabase.IsValidFolder(folderPath))
                 {
                     Debug.LogError($"The specified folder path '{folderPath}' is not valid.");
                     return;
                 }

                 newDna.displayName = dnaName;
                 newDna.description = "Converted DNA Asset for " + dnaName;
                 newDna.defaultValue = 0.5f;





                // Build effects from existing plugins that reference this dna name
                if (_target != null)
                {
                    var plugins = _target.GetPlugins();
                    for (int p = 0; p < plugins.Count; p++)
                    {
                        var plugin = plugins[p];
                        if (plugin == null) continue;
                        var indexesMap = plugin.IndexesForDnaNames;
                        if (indexesMap == null || !indexesMap.TryGetValue(dnaName, out var indices) || indices == null || indices.Count == 0)
                            continue;

                        // Blendshape DNA -> DNAEffect_BlendShape per entry
                        var blendshapePlugin = plugin as BlendshapeDNAConverterPlugin;
                        if (blendshapePlugin != null)
                        {
                            var list = blendshapePlugin.blendshapeDNAConverters;
                            if (list == null) continue;
                            for (int i = 0; i < indices.Count; i++)
                            {
                                int idx = indices[i];
                                if (idx < 0 || idx >= list.Count) continue;
                                var conv = list[idx];
                                if (conv == null) continue;
                                var eff = new DNAEffect_BlendShape
                                {
                                    BlendShapeName = conv.blendshapeToApply
                                };
								eff.EffectName = conv.blendshapeToApply;
                                // Approximate evaluator mapping via min/max only (leave defaults 0..1)
                                // conv.modifyingDNA could be inspected, but we keep linear mapping per spec.
                                newDna.effects.Add(eff);
                            }
                            continue; // go to next plugin
                        }

                        // Bone Pose DNA -> DNAEffect_BonePose per entry
                        var bonePosePlugin = plugin as BonePoseDNAConverterPlugin;
                        if (bonePosePlugin != null)
                        {
                            var list = bonePosePlugin.poseDNAConverters;
                            if (list == null) continue;
                            for (int i = 0; i < indices.Count; i++)
                            {
                                int idx = indices[i];
                                if (idx < 0 || idx >= list.Count) continue;
                                var conv = list[idx];
                                if (conv == null || conv.poseToApply == null) continue;
                                var eff = new DNAEffect_BonePose
                                {
                                    bonePose = conv.poseToApply
                                };
								eff.EffectName = conv.poseToApply.name;
                                // Linear curve/mapping left as default per spec
                                newDna.effects.Add(eff);
                            }
                            continue; // next plugin
                        }

                        // Skeleton modifiers -> BoneTranslate/Rotate/Scale per modifier
                        var skeletonPlugin = plugin as SkeletonDNAConverterPlugin;
                        if (skeletonPlugin != null)
                        {
                            var list = skeletonPlugin.skeletonModifiers;
                            if (list == null) continue;
                            for (int i = 0; i < indices.Count; i++)
                            {
                                int idx = indices[i];
                                if (idx < 0 || idx >= list.Count) continue;
                                var mod = list[idx];
                                if (mod == null) continue;

                                string boneName = mod.hashName;
                                if (string.IsNullOrEmpty(boneName)) continue;

                                // Gather base values
                                float vx = 0f, vy = 0f, vz = 0f;
                                try { if (mod.valuesX != null && mod.valuesX.val != null) vx = mod.valuesX.val.value; } catch {}
                                try { if (mod.valuesY != null && mod.valuesY.val != null) vy = mod.valuesY.val.value; } catch {}
                                try { if (mod.valuesZ != null && mod.valuesZ.val != null) vz = mod.valuesZ.val.value; } catch {}

                                switch (mod.property)
                                {
                                    case SkeletonModifier.SkeletonPropType.Position:
                                        {
											var eff = new DNAEffect_BoneTranslate
											{
												BoneName = boneName,
												Translation = new Vector3(vx, vy, vz),
												minMapping = -1f,
												maxMapping = 1f
                                            };
											eff.EffectName= boneName;
                                            newDna.effects.Add(eff);
                                            break;
                                        }
                                    case SkeletonModifier.SkeletonPropType.Scale:
                                        {
                                            var eff = new DNAEffect_BoneScale
                                            {
                                                BoneName = boneName,
                                                ScaleFactor = new Vector3(vx, vy, vz),
												minMapping = -1f,
												maxMapping = 1f
                                            };
                                            eff.EffectName = boneName;
                                            newDna.effects.Add(eff);
                                            break;
                                        }
                                    case SkeletonModifier.SkeletonPropType.Rotation:
                                        {
                                            Vector3 euler = new Vector3(vx, vy, vz);
                                            float angle = euler.magnitude;
                                            Vector3 axis = angle > 0.0001f ? euler.normalized : Vector3.up; // default up if zero
                                            var eff = new DNAEffect_BoneRotate
                                            {
                                                BoneName = boneName,
                                                RotationAxis = axis,
                                                RotationAngle = angle,
												minMapping = -1f,
												maxMapping = 1f
                                            };
                                            eff.EffectName = boneName;
                                            newDna.effects.Add(eff);
                                            break;
                                        }
                                }
                            }
                            continue;
                        }
                    }
                }
 
                 // Save the new DNA asset to the specified folder
                 string assetPath = System.IO.Path.Combine(folderPath, dnaName + ".asset");
                 AssetDatabase.CreateAsset(newDna, assetPath);
                 AssetDatabase.SaveAssets();
                 AssetDatabase.Refresh();
                 // Cache the newly created DNA asset
                 _foundDnaCache[dnaName] = newDna;
             }
             catch (Exception ex)
             {
                 Debug.LogError($"Failed to convert DNA '{dnaName}': {ex.Message}");
             }
         }

		private bool ConvertedDNAExists(string dnaName, string folderPath)
		{
			// Editor-only lookup for DNA assets by name within the specified folder, with caching
			if (string.IsNullOrEmpty(dnaName) || string.IsNullOrEmpty(folderPath)) return false;

			if (_foundDnaCache.ContainsKey(dnaName))
			{
				var dna = _foundDnaCache[dnaName];
				return dna != null;
			}

			try
			{
				string[] searchInFolders = new[] { folderPath };
				string filter = "t:DNA name:" + dnaName;
				string[] guids = AssetDatabase.FindAssets(filter, searchInFolders);
				for (int i = 0; i < guids.Length; i++)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					var dna = AssetDatabase.LoadAssetAtPath<DNA>(path);
					if (dna != null && dna.name == dnaName)
					{
						_foundDnaCache[dnaName] = dna; // cache found asset
						return true;
					}
				}
			}
			catch { }

			_foundDnaCache[dnaName] = null; // cache as not found
			return false;
		}

		//Draws the 'View' tabs allowing the user to switch between viewing data 'By Plugin' or 'By DNA'
		private void DrawControllersViewTabs()
		{
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

			if (_dnaAsset != null && _dnaAsset.Names.Length > 0)
			{
				namesToDraw = new List<string>(_dnaAsset.Names);
			}
			else
			{
				namesToDraw = inUseNames;
			}

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

				var activeNamesToDraw = DrawDNASearchArea(EditorGUILayout.GetControlRect(), namesToDraw);

				folder = EditorGUILayout.ObjectField("Conversion Folder", folder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
				if (GUILayout.Button("Convert All DNA Names"))
				{
					for (int i = 0; i < activeNamesToDraw.Count; i++)
					{
						ConvertDNA(activeNamesToDraw[i], folderPath);
					}
                }
                if (folder != null)
				{
					folderPath = AssetDatabase.GetAssetPath(folder);
				}
				EditorGUILayout.LabelField("Folder Path: " + folderPath);

				DynamicDNAPlugin plugin;

				for (int i = 0; i < activeNamesToDraw.Count; i++)
				{
					if (!_expandedDNANames.ContainsKey(activeNamesToDraw[i]))
					{
						_expandedDNANames.Add(activeNamesToDraw[i], false);
					}
					GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
					EditorGUI.indentLevel++;
					EditorGUILayout.BeginHorizontal();
					_expandedDNANames[activeNamesToDraw[i]] = EditorGUILayout.Foldout(_expandedDNANames[activeNamesToDraw[i]], activeNamesToDraw[i]);
					GUILayout.FlexibleSpace();
					if (ConvertedDNAExists(activeNamesToDraw[i], folderPath))
					{
						GUILayout.Label("Converted", GUILayout.Width(75));
					}
					else
					{
						GUILayout.Label("Not Converted");
					}
					if (GUILayout.Button("Convert DNA", EditorStyles.miniButton, GUILayout.Width(100)))
					{
						ConvertDNA(activeNamesToDraw[i], folderPath);
					}
					EditorGUILayout.EndHorizontal();
					EditorGUI.indentLevel--;
					GUILayout.EndHorizontal();
					if (_expandedDNANames[activeNamesToDraw[i]])
					{
						GUI.color = new Color(0.75f, 0.875f, 1f, 0.3f);
						GUILayout.BeginVertical(_pluginsByDNAAreaStyle);
						GUI.color = Color.white;
						GUILayout.BeginVertical(_pluginChooserAreaStyle);

						for (int pi = 0; pi < _target.PluginCount; pi++)
						{
							plugin = _target.GetPlugin(pi);
							if (plugin == null) { continue; }
							if (!_pluginsEditors[plugin].UsesDNAMember(activeNamesToDraw[i])) { continue; }
							if (pi > 0) { GUILayout.Space(EditorGUIUtility.standardVerticalSpacing * 2); }
							_pluginsEditors[plugin].OnInspectorForDNAGUI(activeNamesToDraw[i]);
						}

						GUILayout.EndVertical();
						GUILayout.EndVertical();
					}
				}
				GUIHelper.EndVerticalPadded(3);
			}
		}

		private void DrawOverallModifiers()
		{
			var overallModifiersProp = serializedObject.FindProperty("_overallModifiers");
			var overallModsFoldoutRect = EditorGUILayout.GetControlRect();
			overallModsFoldoutRect.height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			var overallModsLabel = EditorGUI.BeginProperty(overallModsFoldoutRect, new GUIContent(overallModifiersProp.displayName), overallModifiersProp);

			overallModsLabel.text = overallModsLabel.text.ToUpper();
			GUIHelper.ToolbarStyleFoldout(overallModsFoldoutRect, overallModsLabel.text.ToUpper(), new string[] { overallModsLabel.tooltip }, ref _overallModifiersExpanded, ref _overallModifiersHelpExpanded);

			if (_overallModifiersExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				GUILayout.Space(5);
				EditorGUILayout.PropertyField(overallModifiersProp);
				GUIHelper.EndVerticalPadded(3);
			}
		}

		#endregion

		#region REORDERABLE LIST CALLBACKS

		private void DrawConverterListHeaderCallback(Rect rect)
		{
			_convertersROL.headerHeight = 0f;
		}

		private float GetConverterListEntryHeightCallback(int index)
		{
			var plugin = _target.GetPlugin(index);
			if (plugin == null) { return 0f; }
			return _pluginsEditors[plugin].GetInspectorHeight();
		}

		private void DrawConverterListEntryCallback(Rect rect, int index, bool isActive, bool isFocused)
		{
			var plugin = _target.GetPlugin(index);
			if (plugin == null) { return; }
			var prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;
			_pluginsEditors[plugin].DrawInspectorGUI(rect);
			EditorGUI.indentLevel = prevIndent;
		}

		private void DrawConverterListFooterCallback(Rect rect)
		{
			DrawAddConverterPopup(rect);
		}

		#endregion

		#region GUI UTILS

		private void DrawAddConverterPopup(Rect position)
		{
			var ROLDefaults = new ReorderableList.Defaults();
			var padding = 4f;
			_availablePlugins = DynamicDNAPlugin.GetAvailablePluginTypes();

			Rect addRect = Rect.zero;
			if (position == Rect.zero)
			{
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
			if (EditorGUI.DropdownButton(addPopupRect, new GUIContent(dropdownLabel, "Add converters of the selected type to the " + _dnaConvertersLabel + " list"), FocusType.Keyboard))
			{
				GenericMenu popupMenu = new GenericMenu();
				AddMenuItemForAddConvertersPopup(popupMenu, null);
				for (int i = 0; i < _availablePlugins.Count; i++)
				{
					AddMenuItemForAddConvertersPopup(popupMenu, _availablePlugins[i]);
				}
				popupMenu.DropDown(addPopupRect);
			}

			EditorGUI.BeginDisabledGroup(_pluginToAdd == null);
			if (GUI.Button(addBtnRect, new GUIContent("Add", (_pluginToAdd == null ? "Choose converters to add first" : ""))))
			{
				_target.AddPlugin(_pluginToAdd);
				_pluginToAdd = null;
				InitPlugins();
			}
			EditorGUI.EndDisabledGroup();

			if (position == Rect.zero)
			{
				GUILayout.EndVertical();
			}
		}

		private void AddMenuItemForAddConvertersPopup(GenericMenu menu, Type pluginType)
		{
			if (pluginType == null)
			{
				var cbObj = new ConverterToChoose(pluginType);
				var selected = _pluginToAdd == null;
				menu.AddItem(new GUIContent("Add Converters..."), selected, OnAddConvertersPopupItemSelected, cbObj);
			}
			else
			{
				var cbObj = new ConverterToChoose(pluginType);
				var selected = (_pluginToAdd != null && _pluginToAdd.Equals(pluginType)) ? true : false;
				menu.AddItem(new GUIContent(pluginType.Name.Replace("Plugin", "") + "s"), selected, OnAddConvertersPopupItemSelected, cbObj);
			}
		}

		private void OnAddConvertersPopupItemSelected(object pluginToChoose)
		{
			_pluginToAdd = ((ConverterToChoose)pluginToChoose).converterType;
		}

		private List<string> DrawDNASearchArea(Rect position, List<string> namesList)
		{
			if (_dnaSearchField == null)
			{
				_dnaSearchField = new UnityEditor.IMGUI.Controls.SearchField();
			}

			_DNASearchString = _dnaSearchField.OnToolbarGUI(position, _DNASearchString);

			if (String.IsNullOrEmpty(_DNASearchString))
			{
				return namesList;
			}

			List<string> filteredNames = new List<string>();
			for (int i = namesList.Count - 1; i >= 0; i--)
			{
				if (namesList[i].IndexOf(_DNASearchString, StringComparison.CurrentCultureIgnoreCase) > -1)
				{
					filteredNames.Add(namesList[i]);
				}
			}

			return filteredNames;
		}

		#endregion

		#region STATIC UTILS

		public static void SetLivePopupEditor(DynamicDNAConverterControllerInspector liveDDCCEditor)
		{
			if (Application.isPlaying)
			{
				_livePopupEditor = liveDDCCEditor;
			}
		}

		public static void DNANamesPopup(Rect position, SerializedProperty property, string selected, DynamicUMADnaAsset DNAAsset)
		{
			if (DNAAsset == null)
			{
				EditorGUI.BeginChangeCheck();
				property.stringValue = EditorGUI.TextField(position, selected);
				if (EditorGUI.EndChangeCheck())
				{
					property.serializedObject.ApplyModifiedProperties();
					GUI.changed = true;
				}
			}
			else
			{
				int selectedIndex = -1;
				var names = GetDNANamesForPopup(DNAAsset);
				selectedIndex = names.IndexOf(selected);
				if (selectedIndex == -1)
				{
					if (!string.IsNullOrEmpty(selected))
					{
						names.Insert(1, selected);
						selectedIndex = 1;
					}
					else
					{
						selectedIndex = 0;
					}
				}
				EditorGUI.BeginChangeCheck();
				selectedIndex = EditorGUI.Popup(position, selectedIndex, names.ToArray());
				if (EditorGUI.EndChangeCheck())
				{
					if (selectedIndex != 0)
					{
						property.stringValue = names[selectedIndex];
					}
					else
					{
						property.stringValue = "";
					}
					property.serializedObject.ApplyModifiedProperties();
					GUI.changed = true;
				}
			}
		}

		private static List<string> GetDNANamesForPopup(DynamicUMADnaAsset DNAAsset)
		{
			var _dnaNamesForPopup = new List<string>();
			for (int i = 0; i < DNAAsset.Names.Length; i++)
			{
				_dnaNamesForPopup.Add(DNAAsset.Names[i]);
			}
			_dnaNamesForPopup.Insert(0, "Choose DNA Name");
			return _dnaNamesForPopup;
		}

		#endregion

		#region SPECIAL TYPES

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
