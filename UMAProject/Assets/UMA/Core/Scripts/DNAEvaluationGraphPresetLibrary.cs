using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif
namespace UMA
{
	//This is an editor only asset that stores a list of preset DNAEvaluationGraphs, its similar to an AnimationCurve Preset Library.
	//A DNAEvaluationGraph field works kind of like an enum of animationCurves and in its popup it shows the presets defined here, 
	//rather like the 'Options' cog in an AnimationCurve editor. This is to make it possible for the user to easily set 'sane' values for their DNAEvaluationGraphs.
	//
	//I really wanted the options that show in the dropdown of choices to also have tooltips to explain what they do, but I didn't want the 
	//actual field to contain that data, so this handles that too
	//
	//The values from the items in this library are used to set the keyframes in the DNAEvaluationGraph's graph value in the same way that an 
	//a curve in an AnimationCurve preset library, is used to set the keyframes for an AnimationCurve, the actual presets are not referenced.
	//This is so that this script/asset does not need to be in the build for a DNAEvaluationGraph field to work, under the hood DNAEvaluationGraph is just a name (string) and graph(AnimationCurve)
	//There is a slight downside to this in that 'editing' a preset here wont change the values of any field that appears to be using it, we could make that happen though if its deemed necessary.
	//
	//I decided to make it possible to create more than one of these libraries because its better if the user doesn't edit one that might in future come with UMA
	//because if they update UMA it will get overridden and they'd loose any of their custom graphs.
	//I chose not to just have an animationCurve field because its really not very easy to understand how the curve works on the incoming dna value

	[System.Serializable]
	public sealed class DNAEvaluationGraphPresetLibrary : ScriptableObject
	{

		[SerializeField]
		private List<DNAEvaluationGraph> _customGraphPresets = new List<DNAEvaluationGraph>();

		[SerializeField]
		private List<string> _customGraphTooltips = new List<string>();
#if UNITY_EDITOR
		private static List<DNAEvaluationGraph> _cachedGlobalList = new List<DNAEvaluationGraph>();

		private static List<string> _cachedGlobalTooltips = new List<string>();
#endif

		/// <summary>
		/// Returns the default list of graph presets shipped with UMA
		/// </summary>
		public static List<DNAEvaluationGraph> DefaultGraphPresets
		{
			get { return new List<DNAEvaluationGraph>(DNAEvaluationGraph.Defaults.Keys); }
		}

		public static List<string> DefaultGraphTooltips
		{
			get { return new List<string>(DNAEvaluationGraph.Defaults.Values); }
		}

		/// <summary>
		/// Returns the list of graph presets stored in this asset
		/// </summary>
		public List<DNAEvaluationGraph> CustomGraphPresets
		{
			get { return _customGraphPresets; }
		}
		/// <summary>
		/// Returns the list of graph presets tooltips stored in this asset
		/// </summary>
		public List<string> CustomGraphTooltips
		{
			get { return _customGraphTooltips; }
		}

#if UNITY_EDITOR

		/// <summary>
		/// Returns all the custom graph presets from all the DNAEvaluatorPresetLibrarys in the project
		/// </summary>
		public static List<DNAEvaluationGraph> AllCustomGraphPresets
		{
			get
			{
				var presetLibs = GetAllInstances();
				var _customsList = new List<DNAEvaluationGraph>();
				for (int i = 0; i < presetLibs.Length; i++)
				{
					if (presetLibs[i]._customGraphPresets.Count > 0)
					{
						_customsList.AddRange(presetLibs[i]._customGraphPresets);
					}
				}
				return _customsList;
			}
		}

		/// <summary>
		/// Returns all the custom graph tooltips from all the DNAEvaluatorPresetLibrarys in the project
		/// </summary>
		public static List<string> AllCustomGraphTooltips
		{
			get
			{
				var presetLibs = GetAllInstances();
				var _customsTooltips = new List<string>();
				for (int i = 0; i < presetLibs.Length; i++)
				{
					if (presetLibs[i]._customGraphTooltips.Count > 0)
					_customsTooltips.AddRange(presetLibs[i]._customGraphTooltips);
				}
				return _customsTooltips;
			}
		}

		/// <summary>
		/// Returns a list of all the graph presets in the project (Default and Custom)
		/// </summary>
		public static List<DNAEvaluationGraph> AllGraphPresets
		{
			get
			{
				if (_cachedGlobalList.Count == 0)
				{
					_cachedGlobalList.AddRange(DefaultGraphPresets);
					_cachedGlobalList.AddRange(AllCustomGraphPresets);
				}
				return _cachedGlobalList;

			}
		}

		/// <summary>
		/// Returns a list of the tooltips for all the graph presets in the project (Default and Custom)
		/// </summary>
		public static List<string> AllGraphTooltips
		{
			get {
				if (_cachedGlobalTooltips.Count == 0)
				{
					_cachedGlobalTooltips.AddRange(DefaultGraphTooltips);
					_cachedGlobalTooltips.AddRange(AllCustomGraphTooltips);
				}
				return _cachedGlobalTooltips;
			}
		}

		/// <summary>
		/// Attempts to find the tooltip for the given evaluation graph from all the graph presets in the project (Default and Custom)
		/// </summary>
		public static string GetTooltipFor(DNAEvaluationGraph graph)
		{
			var ret = graph.name;
			foreach(KeyValuePair<DNAEvaluationGraph, string> kp in DNAEvaluationGraph.Defaults)
			{
				if (kp.Key.GraphMatches(graph))
					return kp.Value;
			}
			var _allCustomPresets = AllCustomGraphPresets;
			for (int i = 0; i < _allCustomPresets.Count; i++)
			{
				if (_allCustomPresets[i].GraphMatches(graph))
					return AllCustomGraphTooltips[i];
			}
			return ret;
		}

		/// <summary>
		/// Adds a new preset to the first found DNAEvaluationGraphPresets library
		/// </summary>
		public static void AddNewPreset(string name, AnimationCurve graph, string tooltip)
		{
			var presetLibs = GetAllInstances();
			for (int i = 0; i < presetLibs.Length; i++)
			{
				if (presetLibs[i].AddNewPreset(graph, name, tooltip))
					break;
			}
		}
		/// <summary>
		/// Add a new preset to this assets DNAEvaluatorPresets list
		/// </summary>
		public bool AddNewPreset(AnimationCurve graph, string name, string tooltip)
		{
			var nameError = "";
			var graphError = "";
			return AddNewPreset(name, tooltip, graph, ref nameError, ref graphError, null);
		}
		/// <summary>
		/// Add a new preset to this assets DNAEvaluatorPresets list
		/// </summary>
		/// <returns>Returns true if the preset was added. False if another preset already existed with the same name or graph</returns>
		public bool AddNewPreset(string name, string tooltip, AnimationCurve graph, ref string nameError, ref string graphError, DNAEvaluationGraph existingGraph = null)
		{
			nameError = "";
			graphError = "";
			bool added = true;
			if (graph == null)
			{
				graphError = "No graph Provided";
				Debug.LogWarning(graphError);
				return false;
			}
			if (string.IsNullOrEmpty(name))
			{
				nameError = "No name provided";
				Debug.LogWarning(nameError);
				return false;
			}
			if (graph.keys.Length < 2)
			{
				graphError = "The new graph must have at least two keys";
				Debug.LogWarning(graphError);
				return false;
			}
			//if we are not updating an existing one
			if (existingGraph == null)
			{
				//make sure there is not already an existing one
				if (!CheckForExistingPreset(name, graph, ref nameError, ref graphError))
				{
					Debug.LogWarning("Graph could not be added");
					if (!string.IsNullOrEmpty(nameError))
					{
						Debug.LogWarning(nameError);
					}
					if (!string.IsNullOrEmpty(graphError))
					{
						Debug.LogWarning(graphError);
					}
					return false;
				}
				_customGraphPresets.Add(new DNAEvaluationGraph(name, graph));
				_customGraphTooltips.Add(tooltip);
			}
			else
			{
				//get the existing one (which might reside in another asset) and update that
				var presetLibs = GetAllInstances();
				int foundIndex = -1;
				for (int i = 0; i < presetLibs.Length; i++)
				{
					foundIndex = -1;
					for (int ci = 0; ci < presetLibs[i]._customGraphPresets.Count; ci++)
					{
						if(presetLibs[i]._customGraphPresets[ci] == existingGraph)
						{
							foundIndex = ci;
							break;
						}
					}
					if(foundIndex >= 0)
					{
						presetLibs[i]._customGraphPresets[foundIndex] = new DNAEvaluationGraph(name, graph);
						presetLibs[i]._customGraphTooltips[foundIndex] = tooltip;
						added = true;
						EditorUtility.SetDirty(presetLibs[i]);
						//AssetDatabase.SaveAssets();
						break;
					}
				}
				if (!added)
				{
					nameError = graphError ="Could not find existsing graph to update";
				}

			}
			if (added)
			{
				//force the global caches to refresh
				_cachedGlobalList.Clear();
				_cachedGlobalTooltips.Clear();
			}
			return added;
		}

		/// <summary>
		/// Returns true if no existing default or custom presets have the same name or graph as the given name and graph
		/// </summary>
		/// <param name="name">The name of the preset to be added</param>
		/// <param name="graph">The graph of the preset to be added</param>
		/// <param name="nameError">Returns an error with the name of the library asset that an existing DNAEvaluationGraph with the same name has been added to</param>
		/// <param name="graphError">Returns an error with the name of the library asset that an existing DNAEvaluationGraph with the same graph has been added to</param>
		/// <returns></returns>
		private bool CheckForExistingPreset(string name, AnimationCurve graph, ref string nameError, ref string graphError)
		{
			nameError = "";
			graphError = "";
			//check the default presets
			for (int di = 0; di < DefaultGraphPresets.Count; di++)
			{
				if (DefaultGraphPresets[di].name == name)
				{
					nameError = "There was already a Default Preset with the name you are attempting to add";
					return false;
				}
				if (DefaultGraphPresets[di].GraphMatches(graph))
				{
					graphError = "Default preset " + DefaultGraphPresets[di].name + " has the same graph as the one you are attempting to add";
					return false;
				}
			}
			var presetLibs = GetAllInstances();
			for (int i = 0; i < presetLibs.Length; i++)
			{
				if (presetLibs[i]._customGraphPresets.Count > 0)
				{
					for (int ci = 0; ci < presetLibs[i]._customGraphPresets.Count; ci++)
					{
						if (presetLibs[i].CustomGraphPresets[ci].name == name)
						{
							nameError = "Custom preset " + presetLibs[i]._customGraphPresets[ci].name + " in preset library named " + presetLibs[i].name + " has the same name as the one you are attempting to add";
							return false;
						}
						if (presetLibs[i]._customGraphPresets[ci].GraphMatches(graph))
						{
							graphError = "Custom preset " + presetLibs[i]._customGraphPresets[ci].name + " in preset library named "+ presetLibs[i].name +" has the same graph as the one you are attempting to add";
							return false;
						}
					}
				}
			}
			return true;
		}
		/// <summary>
		/// Deletes any customPresets using the given graph from all preset libraries in the project
		/// </summary>
		public static void DeleteCustomPreset(DNAEvaluationGraph graphToDelete)
		{
			var presetLibs = GetAllInstances();
			int indexToDel = -1;
			for (int i = 0; i < presetLibs.Length; i++)
			{
				if (presetLibs[i]._customGraphPresets.Count > 0)
				{
					for (int ci = 0; ci < presetLibs[i]._customGraphPresets.Count; ci++)
					{
						if(presetLibs[i]._customGraphPresets[ci] == graphToDelete)
						{
							Debug.Log("Found Delete Target " + graphToDelete.name+" in "+presetLibs[i].name);
							indexToDel = ci;
							break;
						}
					}
					if (indexToDel > -1)
					{
						Debug.Log(graphToDelete.name+" Deleted from list in "+presetLibs[i].name);
						presetLibs[i]._customGraphPresets.RemoveAt(indexToDel);
						try
						{
							presetLibs[i]._customGraphTooltips.RemoveAt(indexToDel);
						}
						catch { }
					}
				}
				if (indexToDel > -1)
				{
					//force the global caches to refresh
					_cachedGlobalList.Clear();
					_cachedGlobalTooltips.Clear();

					EditorUtility.SetDirty(presetLibs[i]);
					AssetDatabase.SaveAssets();
					break;
				}
			}
		}

		private static DNAEvaluationGraphPresetLibrary[] GetAllInstances()
		{
			string[] guids = AssetDatabase.FindAssets("t:" + typeof(DNAEvaluationGraphPresetLibrary).Name); 
			DNAEvaluationGraphPresetLibrary[] a = new DNAEvaluationGraphPresetLibrary[guids.Length];
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				a[i] = AssetDatabase.LoadAssetAtPath<DNAEvaluationGraphPresetLibrary>(path);
			}

			return a;
		}

		#region DEMO FIELDS AND MEHODS

		[SerializeField]
		private DNAEvaluationGraph _exampleField;

		[SerializeField]
		[Tooltip("To make the example work, make sure the dna name is set to 'DNAValue', or not, up to you :)")]
		private DNAEvaluator _exampleEvaluator = new DNAEvaluator("DNAValue", DNAEvaluationGraph.Default, 1f);

		[SerializeField]
		[Range(0f, 1f)]
		private float _exampleDNAValue = 0.5f;

		public float EvaluateDemo()
		{
			var dna = new DynamicUMADna();
			dna._names = new string[1] { "DNAValue" };
			dna._values = new float[1] { _exampleDNAValue };
			return _exampleEvaluator.Evaluate(dna);
		}

		#endregion

		[UnityEditor.MenuItem("UMA/Create DNAEvaluationGraph Presets Library")]
		public static void DNAEvaluatorPresetLibraryAsset()
		{
			//we want to create this in an editor folder
			var path = AssetDatabase.GetAssetPath(Selection.activeObject);
			if (path == "")
			{
				path = "Assets/Editor";
			}
			else if (File.Exists(path)) // modified this line, folders can have extensions.
			{
				path = path.Replace("/" + Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
			}
			if (path.IndexOf("/Editor") < 0)
				path += "/Editor";
			path += "/New DNAEvaluatorPresetLibrary.asset";
			UMA.CustomAssetUtility.CreateAsset<DNAEvaluationGraphPresetLibrary>(path);
		}
#endif

	}
}
