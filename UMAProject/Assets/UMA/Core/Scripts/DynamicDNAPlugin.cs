using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{

	[System.Serializable]
	public abstract class DynamicDNAPlugin : ScriptableObject
	{
		//=====================================================================//
		//A DynamicDNAPlugin is always a LIST of a type of 'DNA Converter' (an abstract concept- it can be any type)
		//A 'DNA Converter' converts dna values into modifications to the character
		//For example in UMA currently we have a SkeletonModifier that converts dna values into modifications to the skeletons bones
		//Or a DNAMorphset converts dna values into weights for UMABonePoses and Blendshapes
		//So those are examples of a 'DNA Converter' and a DynamicDNAPlugin is most basically a list of one of those kinds of things, 
		//along with an ApplyDNA method to apply them
		//The plugin concept places no restrictions on what those 'DNA Converters' might be or do.
		//But it does expect that a DynamicDNAPlugin contain a list of them and requires one method and one property in order to integrate them into the system.

		//DynamicDNAPlugins only have to derive from DynamicDNAPlugin and they do not need an associated inspector. 
		//Any new plugins are automatically found and are available to add to the DynamicDNAConverterControllerAsset via its inspector

		//DynamicDNAPlugins also have a MasterWeight. Setting this to zero disables the plugin, but the master weight can itself be hooked up to a dna value.
		//By doing this different characters can control how much a set of Skeleton Modifiers or MorphSets should apply to them in their current state.
		//For example a charcaters 'Claws' dna might do nothing while its human, but alot when it is a 'werewolf' (i.e. its 'werewolf' dna is turned up)

		//The system also introduces DNAEvaluator, which is a super flexible field that performs math calculations on a dna value using a customizable animation curve.
		//This is so that there is no need to have any extra behaviours or code in order to interpret dna values in a certain way.
		//So the user is not overwhelmed by the complexity of using animation curves for math, DNAEvaluator uses DNAEvaluationGraphs which have preset curves
		//with nice friendly names and tool tips. As coders we can set DNAEvaluationGraph fields to use one of our predefined defaults.

		//DynamicDNAPlugins are assigned to a DynamicDNAConverterControllerAsset which calls ApplyDNA on each plugin in turn at runtime, 
		//and which at edit time looks after the creation of the Plugin Assets and its own Plugins list
		//A DynamicDNAConverterControllerAsset is assigned to a DynamicDNAConverterBehaviour which triggers the ApplyDNA action on the DynamicDNAConverterControllerAsset

		/// <summary>
		/// Does this plugin get applied during the Standard ApplyDNA pass or in the 'Pre Pass'
		/// </summary>
		public enum ApplyPassOpts
		{
			PrePass,
			Standard
		}

		#region ABSTRACT PROPERTIES AND METHODS

		//It is REQUIRED that all DynamicDNAPlugins have these two Propeties and Methods (and thats it!)

		/// <summary>
		/// Returns a dictionary of all the dna names in use by the plugin and the indexes of the entries in its converter list that reference them
		/// </summary>
		public abstract Dictionary<string, List<int>> IndexesForDnaNames { get; }

		/// <summary>
		/// Called by the converter this plugin is assigned to. Applys the plugins list of converters to the character
		/// </summary>
		public abstract void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash);

		#endregion

		#region PUBLIC MEMBERS

		/// <summary>
		/// All DynamicDNAPlugins have a 'MasterWeight' this makes it possible to disable them completely, or only enable them when certain dna conditions are met
		/// </summary>
		[Tooltip("The master weight controls how much all the converters in this group are applied. You can disable a set of converters by making the master weight zero. Or you can hook the master weight up to a characters dna so the converters only apply when that dna has a certain value.")]
		[SerializeField]
		public MasterWeight masterWeight = new MasterWeight();

		#endregion

		#region PRIVATE MEMBERS

		[SerializeField]
		private DynamicDNAConverterController _converterController;

		#endregion

		#region PUPLIC PROPERTIES

		/// <summary>
		/// The converterController asset this plugin has been assigned to. This property is set by the converterBehaviour when it is inspected or starts
		/// </summary>
		public DynamicDNAConverterController converterController
		{
			get { return _converterController; }
			set { _converterController = value; }
		}

		public DynamicUMADnaAsset DNAAsset
		{
			get
			{
				if (_converterController != null)
					return _converterController.DNAAsset;
				return null;
			}
		}

		#endregion

		#region VIRTUAL PROPERTIES

		//Its is OPTIONAL for any DynamicDNAPlugin to override these properties / methods

		/// <summary>
		/// Does this plugin get applied during the Standard ApplyDNA pass or in the 'Pre Pass'
		/// </summary>
		public virtual ApplyPassOpts ApplyPass { get { return ApplyPassOpts.Standard; } }

#if UNITY_EDITOR

		public virtual string PluginHelp { get { return ""; } }

		public virtual float GetListHeaderHeight
		{
			get
			{
				return 0f;
			}
		}

		public virtual float GetListFooterHeight
		{
			get
			{
				return 13f;
			}
		}

		/// <summary>
		/// Gets the height of the 'Help' info that will be drawn by DrawPluginHelp.
		/// </summary>
		public virtual float GetPluginHelpHeight
		{
			get
			{
				//by default this will return the height of a help box that contains the PluginHelp string
				return EditorStyles.helpBox.CalcHeight(new GUIContent(PluginHelp, EditorGUIUtility.FindTexture("console.infoicon")), Screen.width - EditorGUIUtility.FindTexture("console.infoicon").width) + (EditorGUIUtility.singleLineHeight / 2);
			}
		}

		//This is a string array because different plugins might want to make different import methods.
		//the plugins ImportSettings method will just be sent the index of the choice, then its up to the method what to do
		/// <summary>
		/// Standard ImportSettingsMethods are [0]Add [1]Replace
		/// Override this if your plugins ImportSettings method uses different options
		/// </summary>
		public virtual string[] ImportSettingsMethods
		{
			get
			{
				return new string[]
				{
				"Add",
				"Replace"
				};
			}
		}

#endif

		#endregion

		#region VIRTUAL METHODS

		//these all show when anything else gets a DynamicDNAPlugin - I'd prefer them to be protaected but also available to the pluginDrawer..How??

#if UNITY_EDITOR
		/// <summary>
		/// Override this method if DynamicDNAPluginInspector is not finding your list of converters automatically
		/// </summary>
		public virtual SerializedProperty GetConvertersListProperty(SerializedObject pluginSO)
		{
			//if overidden you should do something like 
			//return pluginSO.FindPropertyRelative("nameOfMyConverterList");

			//By default gets the first kind of valid array in the plugin.
			//Since plugins should always be a list of converters first and foremost this should usually work
			SerializedProperty it = pluginSO.GetIterator();
			it.Next(true);
			while (it.Next(false))
			{
				if (it.propertyType != SerializedPropertyType.String && it.isArray && it.name != "Array" && it.name != "_masterWeight")
				{
					return it;
				}
			}
			Debug.LogWarning("Could not find the Converters list for " + this.name + ". Please override 'GetConvertersListProperty' in your plugin");
			return null;
		}

		/// <summary>
		/// Draws the plugins 'Help' info using the value from the plugins 'PluginHelp' property. If you override this method you will also need to override the GetPluginHeight method
		/// </summary>
		public virtual void DrawPluginHelp(Rect position)
		{
			//by default this will draw a helpbox that contains the PluginHelp string
			EditorGUI.HelpBox(position, PluginHelp, MessageType.Info);
		}
		/// <summary>
		/// Override this to draw your own content in the Elements list header
		/// Use pluginSO for to find properties to pass to standard EditorGUI.Property methods etc
		/// You may also want to override GetListHeaderHeight if you need more lines
		/// </summary>
		/// <param name="rect">The full height of the header, override GetListHeaderHeight if you need more lines sent here</param>
		/// <param name="pluginSO">The ScriptableObject representation of the plugin</param>
		/// <returns>True if you want the default elements search bar drawn, false otherwise</returns>
		public virtual bool DrawElementsListHeaderContent(Rect rect, SerializedObject pluginSO)
		{
			return true;
		}

		/// <summary>
		/// Gets an label for an entry from this plugins list of converters
		/// </summary>
		/// <param name="pluginSO">The SerializedObject representation of this plugin</param>
		/// <param name="entryIndex">The index from this plugins list of converters to draw</param>
		public virtual GUIContent GetPluginEntryLabel(SerializedProperty entry, SerializedObject pluginSO, int entryIndex)
		{
			if (entry != null)
			{
				return new GUIContent(entry.displayName);
			}
			return GUIContent.none;
		}

		/// <summary>
		/// Gets the height for an entry from this plugins list of converters.
		/// </summary>
		/// <param name="pluginSO">The SerializedObject representation of this plugin</param>
		/// <param name="entryIndex">The index from this plugins list of converters to draw</param>
		public virtual float GetPluginEntryHeight(SerializedObject pluginSO, int entryIndex, SerializedProperty entry)
		{
			if(entry != null)
			{
				if (entry.isExpanded)
					return EditorGUI.GetPropertyHeight(entry, true);
				else
					return EditorGUIUtility.singleLineHeight;
			}
			return EditorGUIUtility.singleLineHeight;
		}

		/// <summary>
		/// Draws an entry from this plugins list of converters in the UI. If you override this you may also need to override GetPluginEntryHeight.
		/// </summary>
		/// <param name="pluginSO">The SerializedObject representation of this plugin</param>
		/// <param name="entryIndex">The index from this plugins list of converters to draw</param>
		/// <param name="isExpanded">Whether the entry is currently expanded</param>
		/// <returns>whether the entry is still expanded</returns>
		public virtual bool DrawPluginEntry(Rect rect, SerializedObject pluginSO, int entryIndex, bool isExpanded, SerializedProperty entry)
		{
			if (entry != null)
			{
				EditorGUI.PropertyField(rect, entry, GetPluginEntryLabel(entry, pluginSO, entryIndex), true);
				return entry.isExpanded;
			}
			return false;
		}

		/// <summary>
		/// A callback that is called *after* a new entry is added to the plugins list of converters
		/// </summary>
		/// <param name="pluginSO">The SerializedObject representation of this plugin</param>
		/// <param name="entryIndex">The index from this plugins list of converters to that was added</param>
		public virtual void OnAddEntryCallback(SerializedObject pluginSO, int entryIndex)
		{
			//do nothing
		}

		/// <summary>
		/// A callback that is called *before* an entry will be deleted from the plugins list of converters
		/// </summary>
		/// <param name="pluginSO">The SerializedObject representation of this plugin</param>
		/// <param name="entryIndex">The index from this plugins list of converters that will be deleted/param>
		/// <returns>Returns true if the entry can safely be deleted</returns>
		public virtual bool OnRemoveEntryCallback(SerializedObject pluginSO, int entryIndex)
		{
			return true;
		}

		/// <summary>
		/// Override this to draw your own content in the Elements list footer
		/// Use pluginSO for to find properties to pass to standard EditorGUI.Property methods etc
		/// You may also want to override GetListFooterHeight if you need more lines
		/// </summary>
		/// <param name="rect">The full height of the footer, override GetListFooterHeight if you need more lines sent here</param>
		/// <param name="pluginSO">The ScriptableObject representation of the plugin</param>
		/// <returns>True if you want the default elements '+/-' add/remove controls drawn, false otherwise</returns>
		public virtual bool DrawElementsListFooterContent(Rect rect, SerializedObject pluginSO)
		{
			return true;
		}

		/// <summary>
		/// Import settings from another plugin. You need to override this method to enable this functionality in your plugin
		/// </summary>
		/// <param name="pluginToImport">The sent UnityEngine.Object. Your plugin script should first check that this plugin is the correct type</param>
		/// <returns>True if the settings imported successfully</returns>
		public virtual bool ImportSettings(UnityEngine.Object pluginToImport, int importMethod)
		{
			Debug.LogWarning("Import Settings was not implimented for this plugin");
			return false;
		}

#endif
		#endregion

		#region PRIVATE STATIC FIELDS

		private static readonly Type baseDynamicDNAPluginType = typeof(DynamicDNAPlugin);

		private static List<Type> _pluginTypes;

		#endregion

		#region PUBLIC STATIC METHODS

		public static List<Type> GetAvailablePluginTypes()
		{
			if (_pluginTypes == null)
			{
				CompilePluginTypesList();
			}
			return _pluginTypes;
		}

		public static bool IsValidPluginType(Type type)
		{
			return PluginDerivesFromBase(type);
		}

		public static bool IsValidPlugin(UnityEngine.Object asset)
		{
			try
			{
				return PluginDerivesFromBase(asset.GetType());
			}
			catch
			{
				return false;
			}
		}

		#endregion

		#region PRIVATE STATIC METHODS

		private static bool PluginDerivesFromBase(Type type)
		{
			if (type == baseDynamicDNAPluginType)
			{
				return false;
			}
			else
			{
				return baseDynamicDNAPluginType.IsAssignableFrom(type);
			}
		}

		private static void CompilePluginTypesList()
		{
			var list = new List<Type>();
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (var type in assembly.GetTypes())
				{
					if (type.IsAbstract) continue;
					if (PluginDerivesFromBase(type))
					{
						list.Add(type);
					}
				}
			}
			_pluginTypes = list;
		}

		#endregion

		#region SPECIAL TYPES

		[System.Serializable]
		public class MasterWeight
		{
			public enum MasterWeightType
			{
				UseGlobalValue,
				UseDNAValue
			}

			[Tooltip("Choose whether to use a global value for all characters that use this converter, or a dnaValue that characters can change.")]
			[SerializeField]
			private MasterWeightType _masterWeightType = MasterWeightType.UseGlobalValue;

			[Tooltip("The global weight to use for this set of converters. Applies to all characters that use the converter behaviour this resides in. Override this with DNAForWeight for 'per character' control")]
			[Range(0f, 1f)]
			[SerializeField]
			private float _globalWeight = 1f;

			[Tooltip("If set, the weight value will be controlled by the given dna on the character.")]
			[SerializeField]
			[DNAEvaluator.Config(true, true)]
			private DNAEvaluator _DNAForWeight = new DNAEvaluator("", DNAEvaluationGraph.Raw, 1);

			public MasterWeightType masterWeightType
			{
				get { return _masterWeightType; }
				set { _masterWeightType = value; }
			}

			public float globalWeight
			{
				get { return _globalWeight; }
				set { _globalWeight = value; }
			}

			public string dnaName
			{
				get { return _DNAForWeight.dnaName; }
				set { _DNAForWeight.dnaName = value; }
			}

			public DNAEvaluationGraph dnaEvaluationGraph
			{
				get { return _DNAForWeight.evaluator; }
				set { _DNAForWeight.evaluator = value; }
			}

			public float dnaMultiplier
			{
				get { return _DNAForWeight.multiplier; }
				set { _DNAForWeight.multiplier = value; }
			}

			public MasterWeight()
			{
				_masterWeightType = MasterWeightType.UseGlobalValue;
				_globalWeight = 1f;
			}

			public MasterWeight(MasterWeight other)
			{
				_masterWeightType = other._masterWeightType;
				_globalWeight = other._globalWeight;
				_DNAForWeight = new DNAEvaluator(other._DNAForWeight);
			}

			public MasterWeight(MasterWeightType masterWeightType = MasterWeightType.UseGlobalValue, float defaultWeight = 1f, string dnaForWeightName = "", DNAEvaluationGraph dnaForWeightGraph = null, float dnaForWeightMultiplier = 1f)
			{
				_masterWeightType = masterWeightType;
				_globalWeight = defaultWeight;
				if (!string.IsNullOrEmpty(dnaForWeightName))
				{
					_DNAForWeight = new DNAEvaluator(dnaForWeightName, dnaForWeightGraph, dnaForWeightMultiplier);
				}
				else
				{
					_DNAForWeight = new DNAEvaluator("", DNAEvaluationGraph.Raw, 1);
				}
			}

			public float GetWeight(UMADnaBase umaDna = null)
			{
				if (_masterWeightType == MasterWeightType.UseDNAValue)
				{
					return _DNAForWeight.Evaluate(umaDna);
				}
				else
					return _globalWeight;
			}

			//TODO check if this still screws up the incoming dnas values
			public UMADnaBase GetWeightedDNA(UMADnaBase incomingDna)
			{
				if (_masterWeightType == MasterWeightType.UseGlobalValue)
					return incomingDna;

				var masterWeight = GetWeight(incomingDna);
				var weightedDNA = new DynamicUMADna();
				if (masterWeight > 0)
				{
					weightedDNA._names = new string[incomingDna.Names.Length];
					Array.Copy(incomingDna.Names, weightedDNA._names, incomingDna.Names.Length);
					weightedDNA._values = new float[incomingDna.Values.Length];
					Array.Copy(incomingDna.Values, weightedDNA._values, incomingDna.Values.Length);
					for (int i = 0; i < incomingDna.Count; i++)
					{
						weightedDNA.SetValue(i, weightedDNA.GetValue(i) * masterWeight);
					}
				}
				return weightedDNA;
			}
		}

		#endregion
	}
}
