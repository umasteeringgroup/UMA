using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA;

namespace UMA.Editors
{
	[CustomEditor(typeof(DNAEvaluationGraphPresetLibrary))]
	public class DNAEvaluationGraphPresetLibraryInspector : Editor
	{

		DNAEvaluationGraphPresetLibrary _target;

		DNAEvaluationGraphPropertyDrawer dnaEvalDrawer = new DNAEvaluationGraphPropertyDrawer();

		bool extraInfoIsExpanded = false;

		bool defaultPresetsIsExpanded = true;

		bool customPresetsIsExpanded = true;

		bool addPresetIsExpanded = false;

		private float entryHeight = EditorGUIUtility.singleLineHeight * 2f;

		private float padding = EditorGUIUtility.standardVerticalSpacing * 2f;

		private string newGraphName = "";

		private string newGraphTooltip = "";

		private AnimationCurve newGraph;

		private GUIStyle wordwrappedTextArea;

		private bool initialized;

		private string nameError = "";
		private string graphError = "";
		private string addSuccess = "";

		private float swatchButWidth = 20f;

		private List<DNAEvaluationGraph> _customPresets = new List<DNAEvaluationGraph>();

		private List<string> _customPresetTooltips = new List<string>();

		private DNAEvaluationGraph _updatatingPreset = null;

		void OnEnable()
		{
			//Init();//EditorStyles are not here yet on domain reload
			ResetNewGraphFields();
			initialized = false;
		}

		private void Init()
		{
			if (!initialized)
			{
				_target = serializedObject.targetObject as DNAEvaluationGraphPresetLibrary;
				if (newGraph == null)
				{
					newGraph = new AnimationCurve(DNAEvaluationGraph.Default.GraphKeys);
				}
				wordwrappedTextArea = new GUIStyle(EditorStyles.textArea);
				wordwrappedTextArea.wordWrap = true;
				_customPresets = DNAEvaluationGraphPresetLibrary.AllCustomGraphPresets;
				_customPresetTooltips = DNAEvaluationGraphPresetLibrary.AllCustomGraphTooltips;
				initialized = true;
			}
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			Init();

			EditorGUILayout.LabelField("DNAEvaluationGraph Preset Library", EditorStyles.boldLabel);

			EditorGUILayout.HelpBox("A DNAEvaluationGraph Preset Library contains presets for graphs that will be available in a DNAEvaluationGraph field's dropdown list of available graphs. This asset does not need to be included in your build", MessageType.Info);

			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Space(10f);
			extraInfoIsExpanded = EditorGUILayout.Foldout(extraInfoIsExpanded, "What is a DNAEvaluationGraph?", true);
			EditorGUILayout.EndHorizontal();
			if (extraInfoIsExpanded)
			{
				DrawFullHelp();
			}
			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Space(10f);
			defaultPresetsIsExpanded = EditorGUILayout.Foldout(defaultPresetsIsExpanded, "Default Presets", true);
			EditorGUILayout.EndHorizontal();
			if (defaultPresetsIsExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUILayout.HelpBox("These are the standard evaluation graphs that ship with UMA. You can add your own in the custom section below.", MessageType.Info);
				for (int i = 0; i < DNAEvaluationGraphPresetLibrary.DefaultGraphPresets.Count; i++)
				{
					var rect = EditorGUILayout.GetControlRect(false, entryHeight + (padding * 2f));
					rect.x += padding;
					rect.width -= padding * 2f;
					Rect swatchRect = new Rect(rect.xMin, rect.yMin + padding, rect.width, rect.height - (padding * 2f));
					dnaEvalDrawer.DrawSwatch(swatchRect, DNAEvaluationGraphPresetLibrary.DefaultGraphPresets[i], DNAEvaluationGraphPresetLibrary.DefaultGraphTooltips[i], false, false, (dnaGraph) =>
					{
						newGraph = new AnimationCurve(DNAEvaluationGraphPresetLibrary.DefaultGraphPresets[i].GraphKeys);
						addPresetIsExpanded = true;
					}
					);
			}
				GUIHelper.EndVerticalPadded(3);
			}

			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Space(10f);
			customPresetsIsExpanded = EditorGUILayout.Foldout(customPresetsIsExpanded, "Custom Presets",true);
			EditorGUILayout.EndHorizontal();
			if (customPresetsIsExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUILayout.HelpBox("Add your own graph presets to this section using the tools below.", MessageType.Info);
				List<int> custToDel = new List<int>();
				for (int i = 0; i < _customPresets.Count; i++)
				{
					var rect = EditorGUILayout.GetControlRect(false, entryHeight + (padding * 2f));
					rect.x += padding;
					rect.width -= padding * 2f;
					Rect swatchRect = new Rect(rect.xMin, rect.yMin + padding, rect.width - (swatchButWidth + padding), rect.height - (padding * 2f));
					dnaEvalDrawer.DrawSwatch(swatchRect, _customPresets[i], _customPresetTooltips[i]+" [Click To Edit]", false, false, (dnaGraph) =>
					{
						newGraphName = _customPresets[i].name;
						newGraphTooltip = _customPresetTooltips[i];
						newGraph = new AnimationCurve(_customPresets[i].GraphKeys);
						addPresetIsExpanded = true;
						_updatatingPreset = _customPresets[i];
					}
					);
					//editCallback

					var delRect = new Rect(swatchRect.xMax, swatchRect.yMin + padding, swatchButWidth, swatchButWidth);
					if(GUI.Button(delRect, new GUIContent("X", "Delete "+ _customPresets[i].name)))
					{
						custToDel.Add(i);
					}
				}
				if(custToDel.Count > 0)
				{
					for (int i = 0; i < custToDel.Count; i++)
					{
						DNAEvaluationGraphPresetLibrary.DeleteCustomPreset(_customPresets[custToDel[i]]);
					}
					initialized = false;
					EditorUtility.SetDirty(_target);
					AssetDatabase.SaveAssets();
				}
				DrawDNAEvaluationGraphAddBox();
				GUIHelper.EndVerticalPadded(3);
			}
			else
			{
				ResetMessages();
			}

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawDNAEvaluationGraphAddBox()
		{
			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));

			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Space(10f);
			var label = _updatatingPreset ? "Update '" + newGraphName + "' Preset" : "Add a new Preset";
			addPresetIsExpanded = EditorGUILayout.Foldout(addPresetIsExpanded, label, true);
			EditorGUILayout.EndHorizontal();
			if (addPresetIsExpanded)
			{
				EditorGUILayout.Space();

				EditorGUI.BeginChangeCheck();

				if (_updatatingPreset != null)
				{
					//warn the user that updating a preset wont update any fields that created their values from it
					EditorGUILayout.HelpBox("Note: Updating this preset will not update the graphs in any existing DNAEvaluationGraph fields", MessageType.Warning);
				}

				newGraphName = EditorGUILayout.TextField("Preset Name", newGraphName);

				var descRect = EditorGUILayout.GetControlRect(false, 45);
				newGraphTooltip = EditorGUI.TextArea(descRect, newGraphTooltip, wordwrappedTextArea);
				//I want the placeholder text in here
				if(newGraphTooltip == "")
				{
					EditorGUI.BeginDisabledGroup(true);
					EditorGUI.TextArea(descRect, "Preset Tooltip", wordwrappedTextArea);
					EditorGUI.EndDisabledGroup();
				}

				var graphRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight * 2f);

				if (EditorGUI.EndChangeCheck())
				{
					ResetMessages();
				}

				newGraph = EditorGUI.CurveField(graphRect, "Preset Graph", newGraph);

				EditorGUILayout.Space();
				var btnRect = EditorGUILayout.GetControlRect();
				btnRect.width = btnRect.width / 2;
				var clearBtnRect = new Rect(btnRect.xMax, btnRect.yMin, btnRect.width, btnRect.height);
				var addBtnLabel = "Add It!";
				if (_updatatingPreset != null)
				{
					addBtnLabel = "Update '" + newGraphName+"'";
				}
				if (GUI.Button(btnRect, addBtnLabel))
				{
					if (_target.AddNewPreset(newGraphName, newGraphTooltip, newGraph, ref nameError, ref graphError, _updatatingPreset))
					{
						serializedObject.Update();
						serializedObject.ApplyModifiedProperties();
						addSuccess = _updatatingPreset ? "Updated Graph "+newGraphName+" Successfully!" : "New Custom Graph " + newGraphName + " added Successfully!";
						ResetNewGraphFields();
						initialized = false;//force the cached custom list to Update
						EditorUtility.SetDirty(_target);
						AssetDatabase.SaveAssets();
						Repaint();
					}
				}
				if(GUI.Button(clearBtnRect, "Reset Fields"))
				{
					ResetNewGraphFields();
				}
				if (graphError != "" || nameError != "")
				{
					EditorGUILayout.HelpBox("There were the following issues when trying to add your graph:", MessageType.None);
					if (nameError != "")
						EditorGUILayout.HelpBox(nameError, MessageType.Error);
					if (graphError != "")
						EditorGUILayout.HelpBox(graphError, MessageType.Error);
				}
				else if (addSuccess != "")
				{
					EditorGUILayout.HelpBox(addSuccess, MessageType.Info);
				}
			}
			else
			{
				ResetMessages();
			}

			GUIHelper.EndVerticalPadded(3);
		}

		private void ResetMessages()
		{
			addSuccess = "";
			nameError = "";
			graphError = "";
		}

		private void ResetNewGraphFields()
		{
			newGraphName = "";
			newGraphTooltip = "";
			newGraph = new AnimationCurve(DNAEvaluationGraph.Default.GraphKeys);
			nameError = "";
			graphError = "";
			_updatatingPreset = null;
		}

		private void DrawFullHelp()
		{
			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));

			EditorGUILayout.HelpBox("A DNAEvaluationGraph is an option you can choose in a DNAEvaluator field that many UMA DNA Converters use when interpreting your avatars dna values. Its one of these:", MessageType.None);

			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUI.indentLevel++;
			var exampleFieldProp = serializedObject.FindProperty("_exampleField");
			EditorGUILayout.PropertyField(exampleFieldProp);
			EditorGUI.indentLevel--;
			GUIHelper.EndVerticalPadded(3);
			EditorGUILayout.HelpBox("The graph is used to perform math operations on an incoming dna value. UMA comes with a set of DNAEvaluationGraphs built in. But if you need something sepcial you can add it below. Give it a go! If you add a graph in the 'CustomGraphs' area below and then click the dropdown in the field above you will see it is in there as an option. This will be the same for all DNAPlugins that use this field.", MessageType.None);
			EditorGUILayout.HelpBox("For example, below is a DNA called 'DNAValue'. Below that is a DNAEvaluator, using a graph, that will evaluate 'DNAValue'. Try changing the settings in the Evaluator and see how they affect the calculated 'Result' in the last field", MessageType.None);

			GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUI.indentLevel++;
			var exampleEvaluatorProp = serializedObject.FindProperty("_exampleEvaluator");
			exampleEvaluatorProp.isExpanded = true;
			var exampleDNAProp = serializedObject.FindProperty("_exampleDNAValue");
			EditorGUI.BeginChangeCheck();
			exampleDNAProp.floatValue = EditorGUILayout.Slider(new GUIContent("DNAValue"), exampleDNAProp.floatValue, 0f, 1f);
			EditorGUILayout.Space();
			EditorGUILayout.PropertyField(exampleEvaluatorProp);
			if (EditorGUI.EndChangeCheck())
				serializedObject.ApplyModifiedProperties();
			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.FloatField(new GUIContent("Evaluated Result!"), _target.EvaluateDemo());
			EditorGUI.EndDisabledGroup();
			EditorGUI.indentLevel--;
			GUIHelper.EndVerticalPadded(5);

			EditorGUILayout.HelpBox("The Evaluation Graph field itself is a value field rather than a reference field, so once its been set to use a certain graph that graph will not change even if you change the preset that set it. Think of it like Color.red, Color.blue etc, if Unity changed the hues of those colors, any colors that you set using those defaults, would not actually change. So there is no actual need to include this preset library in your project. Its just here to help.", MessageType.None);

			GUIHelper.EndVerticalPadded(3);
		}


	}
}
