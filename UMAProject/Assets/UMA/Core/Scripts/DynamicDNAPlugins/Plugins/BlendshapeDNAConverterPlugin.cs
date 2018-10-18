using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UMA.PoseTools;

namespace UMA
{
	[System.Serializable]
	public class BlendshapeDNAConverterPlugin : DynamicDNAPlugin
	{
		#region FIELDS

		[SerializeField]
		private List<BlendshapeDNAConverter> _blendshapeDNAConverters = new List<BlendshapeDNAConverter>();

		#endregion

		#region PUBLIC PROPERTIES

		public List<BlendshapeDNAConverter> blendshapeDNAConverters
		{
			get { return _blendshapeDNAConverters; }
			set { _blendshapeDNAConverters = value; }
		}

		#endregion

		#region REQUIRED DYNAMICDNAPLUGIN PROPERTIES

		

		public override string PluginHelp
		{
			get { return "Blendshape DNA Converters convert the set dna names into weight settings for Blendshape that will be applied to a character. Normally you will set the 'Default Shape Weight' to 0 so that the blendshape is only applied when one of the set 'Modifier DNAs' returns a suitable value. However for a 'Starting Blendshape' you can set the 'Default Shape Weight' to 1. Optionally, you can link the 'Default Shape Weight' to a dna value on each character by setting up the 'Shape Weight DNA'"; }
		}

		#endregion

		#region REQUIRED DYNAMICDNAPLUGIN METHODS

		/// <summary>
		/// Returns a dictionary of all the dna names in use by the plugin and the entries in its converter list that reference them
		/// </summary>
		/// <returns></returns>
		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				var dict = new Dictionary<string, List<int>>();
				for (int i = 0; i < _blendshapeDNAConverters.Count; i++)
				{
					for (int ci = 0; ci < _blendshapeDNAConverters[i].UsedDNANames.Count; ci++)
					{
						if (!dict.ContainsKey(_blendshapeDNAConverters[i].UsedDNANames[ci]))
							dict.Add(_blendshapeDNAConverters[i].UsedDNANames[ci], new List<int>());

						dict[_blendshapeDNAConverters[i].UsedDNANames[ci]].Add(i);
					}
				}
				return dict;
			}
		}

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			for (int i = 0; i < _blendshapeDNAConverters.Count; i++)
			{
				_blendshapeDNAConverters[i].ApplyDNA(umaData, skeleton, dnaTypeHash);
			}
		}

		#endregion

		#region INSPECTOR GUI OVERRIDES

#if UNITY_EDITOR

		public override string[] ImportSettingsMethods
		{
			get
			{
				return new string[]
				{
				"Add",
				"Replace",
				"Overwrite",
				"AddOverwrite"
				};
			}
		}

		public override bool ImportSettings(UnityEngine.Object pluginToImport, int importMethod)
		{
			var importPlug = pluginToImport as BlendshapeDNAConverterPlugin;
			if (importPlug == null)
			{
				Debug.LogWarning("The plugin you are trying to import was not a BlendshapeDNAConverterPlugin!");
				return false;
			}
			if (importPlug._blendshapeDNAConverters.Count == 0)
			{
				Debug.LogWarning("The plugin you are trying to import had no settings!");
				return false;
			}
			//Method Replace
			if (importMethod == 1)//default importMethods Replace
			{
				_blendshapeDNAConverters.Clear();
			}
			bool existed = false;
			string ovrBS = "";
			DNAEvaluator ovrEval = null;
			for (int i = 0; i < importPlug._blendshapeDNAConverters.Count; i++)
			{
				existed = false;
				ovrBS = "";
				//if method is Add check there is no existing
				//if method is Overwrite or AddOverwrite check if there is an existing entry with values we want to overwrite
				if (importMethod == 0 || importMethod == 2 || importMethod == 3)
				{
					ovrBS = importPlug._blendshapeDNAConverters[i].blendshapeToApply;
					ovrEval = importPlug._blendshapeDNAConverters[i].defaultShapeWeightDNA;
					for (int ii = 0; ii < _blendshapeDNAConverters.Count; ii++)
					{
						//this will only import one modifier that is using this pose is that right?
						//dont think so- TODO BlendshapeDNAConverter needs an equality comparer
						if (_blendshapeDNAConverters[ii].blendshapeToApply == ovrBS && _blendshapeDNAConverters[ii].defaultShapeWeightDNA == ovrEval)
						{
							if (importMethod == 2 || importMethod == 3)
							{
								_blendshapeDNAConverters[ii].defaultShapeWeightDNA = new DNAEvaluator(importPlug._blendshapeDNAConverters[i].defaultShapeWeightDNA);
								_blendshapeDNAConverters[ii].defaultShapeWeight = importPlug._blendshapeDNAConverters[i].defaultShapeWeight;
								_blendshapeDNAConverters[ii].onMissingShapeDNA = importPlug._blendshapeDNAConverters[i].onMissingShapeDNA;
								bool foundModifier = false;
								string importReducerDNA = "";
								for (int ri = 0; ri < importPlug._blendshapeDNAConverters[i].modifierDnas.Count; ri++)
								{
									foundModifier = false;
									importReducerDNA = importPlug._blendshapeDNAConverters[i].modifierDnas[ri].dnaName;
									if (!string.IsNullOrEmpty(importReducerDNA))
									{
										for (int rii = 0; rii < _blendshapeDNAConverters[ii].modifierDnas.Count; rii++)
										{
											if (_blendshapeDNAConverters[ii].modifierDnas[rii].dnaName == importReducerDNA)
											{
												foundModifier = true;
												_blendshapeDNAConverters[ii].modifierDnas[rii].evaluator = new DNAEvaluationGraph(importPlug._blendshapeDNAConverters[i].modifierDnas[ri].evaluator);
												_blendshapeDNAConverters[ii].modifierDnas[rii].multiplier = importPlug._blendshapeDNAConverters[i].modifierDnas[ri].multiplier;
											}
										}
										if (!foundModifier)
										{
											_blendshapeDNAConverters[ii].modifierDnas.Add(new DNAEvaluator(importPlug._blendshapeDNAConverters[i].modifierDnas[ri]));
										}
									}
								}
							}
							existed = true;
						}
					}
				}
				if (!existed && importMethod != 2)//if importmethod was not overwrite
				{
					_blendshapeDNAConverters.Add(new BlendshapeDNAConverter(importPlug._blendshapeDNAConverters[i]));
				}
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			return true;
		}

#endif
		#endregion

		#region SPECIAL TYPES

		[System.Serializable]
		public class BlendshapeDNAConverter
		{
			public enum onMissingShapeDNAOpts { UseGlobalWeight, UseZero }

			#region FIELDS

			[Tooltip("The Blendshape to apply to the character.")]
			[SerializeField]
			private string _blendshapeToApply;

			[SerializeField]
			[Tooltip("Make the default weight 1 to apply the blendshape on start, 0 so the blendshape is only applied by 'Modifying DNA' below. Or you can hook the weight up to a dna on the character to control this dynamically.")]
			private DynamicDefaultWeight _startingShapeWeight = new DynamicDefaultWeight();

			//TODO DITCH THE FOLLOWING AND FIX METHODS
			[SerializeField]
			[Tooltip("The default weight for the Blendshape when no dna is applied. Usually this is zero, but for a 'Starting Blendshape' set this value to 1. NOTE: Changing this value affects all characters that use the same converter behaviour. Set up a 'Default Shape Weight DNA' (below) to affect the default weight 'per character'.")]
			[Range(0f, 1f)]
			[HideInInspector]
			private float _defaultShapeWeight = 0f;

			[SerializeField]
			[Tooltip("A DNA to use for setting the 'Default Shape Weight' (above)")]
			[DNAEvaluator.Config(true)]
			[HideInInspector]
			private DNAEvaluator _defaultShapeWeightDNA;

			[SerializeField]
			[HideInInspector]
			[Tooltip("If the 'Default Shape Weight DNA'  is assigned but not available, should the 'Default Shape Weight' be used or zero?")]
			private onMissingShapeDNAOpts _onMissingShapeDNA = onMissingShapeDNAOpts.UseGlobalWeight;

			[SerializeField]
			[Tooltip("Add dna(s) here that will change the amount that this blendshape is applied depending on their evaluated value.")]
			private DNAEvaluatorList _modifyingDNA = new DNAEvaluatorList();

			#endregion

			#region PRIVATE VARS

			private float _liveShapeWeight = 0f;

			private int _dnaIndex = -1;

			private DynamicUMADnaBase _activeDNA;

			#endregion

			#region PUBLIC PROPERTIES

			public string blendshapeToApply
			{
				get { return _blendshapeToApply; }
				set { _blendshapeToApply = value; }
			}

			public float defaultShapeWeight
			{
				get { return _defaultShapeWeight; }
				set { _defaultShapeWeight = value; }
			}

			public onMissingShapeDNAOpts onMissingShapeDNA
			{
				get { return _onMissingShapeDNA; }
				set { _onMissingShapeDNA = value; }
			}

			public DNAEvaluator defaultShapeWeightDNA
			{
				get { return _defaultShapeWeightDNA; }
				set { _defaultShapeWeightDNA = value; }
			}

			public DNAEvaluatorList modifierDnas
			{
				get { return _modifyingDNA; }
				set { _modifyingDNA = new DNAEvaluatorList(value); }
			}

			//The following properties are handy for Timeline

			/// <summary>
			/// Gets/Sets the dnaName that the defaultPoseWeightDNA evaluator uses. If no dnaEvaluator has been assigned to the 'defaultPoseWeightDNA' field one will be created using the 'default' evaluation settings.
			/// You can use this to temporarily change the dna that is affecting the blendshape weight for special effects
			/// </summary>
			public string DefaultShapeWeightDNAName
			{
				get
				{
					if (_defaultShapeWeightDNA != null)
						return _defaultShapeWeightDNA.dnaName;
					else
						return "";
				}
				set
				{
					if (_defaultShapeWeightDNA == null)
						_defaultShapeWeightDNA = new DNAEvaluator(value, DNAEvaluationGraph.Default, 1f);
					else
						_defaultShapeWeightDNA.dnaName = value;
				}
			}

			/// <summary>
			/// Gets/Sets the multiplier that the defaultShapeWeightDNA evaluator uses. 
			/// You can use this to turn the blendshape Weight up or down independently of dna value for special effects
			/// </summary>
			public float DefaultShapeWeightDNAMultiplier
			{
				get
				{
					if (_defaultShapeWeightDNA != null)
						return _defaultShapeWeightDNA.multiplier;
					else
						return 0f;
				}
				set
				{
					if (_defaultShapeWeightDNA != null)
						_defaultShapeWeightDNA.multiplier = value;
				}
			}

			#endregion

			#region CONSTRUCTOR

			public BlendshapeDNAConverter() { }

			public BlendshapeDNAConverter(string blendshapeToApply, float defaultShapeWeight = 0f, DNAEvaluator defaultShapeWeightDNA = null)
			{
				this._blendshapeToApply = blendshapeToApply;
				this._defaultShapeWeight = defaultShapeWeight;
				this._defaultShapeWeightDNA = new DNAEvaluator(defaultShapeWeightDNA);
			}

			public BlendshapeDNAConverter(BlendshapeDNAConverter other)
			{
				this._blendshapeToApply = other.blendshapeToApply;
				this._defaultShapeWeight = other.defaultShapeWeight;
				this._defaultShapeWeightDNA = new DNAEvaluator(other.defaultShapeWeightDNA);
				this.onMissingShapeDNA = other.onMissingShapeDNA;
				this._modifyingDNA = new DNAEvaluatorList(other.modifierDnas);
			}

			#endregion

			#region METHODS

			//TODO methods for screwing with the reducer dnas? Better if we can use fancy properties with indexers

			public List<string> UsedDNANames
			{
				get
				{
					var usedNames = new List<string>();
					if (!string.IsNullOrEmpty(_defaultShapeWeightDNA.dnaName))
						usedNames.Add(_defaultShapeWeightDNA.dnaName);
					for (int i = 0; i < _modifyingDNA.Count; i++)
					{
						if (!string.IsNullOrEmpty(_modifyingDNA[i].dnaName))
							usedNames.Add(_modifyingDNA[i].dnaName);
					}
					return usedNames;
				}
			}

			public void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
			{
				_liveShapeWeight = _defaultShapeWeight;
				//we can cast to this because the dna field in the converter wont accept anything else
				_activeDNA = (DynamicUMADnaBase)umaData.GetDna(dnaTypeHash);

				if (!string.IsNullOrEmpty(_defaultShapeWeightDNA.dnaName))
				{
					_dnaIndex = System.Array.IndexOf(_activeDNA.Names, _defaultShapeWeightDNA.dnaName);
					if (_dnaIndex > -1)
					{
						_liveShapeWeight = _defaultShapeWeightDNA.Evaluate(_activeDNA.GetValue(_dnaIndex));
					}
					else
					{
						//dna not found obey the _onMissingShapeDNA option
						if (_onMissingShapeDNA == onMissingShapeDNAOpts.UseZero)
							_liveShapeWeight = 0f;
					}
				}

				_liveShapeWeight += _modifyingDNA.Evaluate(_activeDNA);
				_liveShapeWeight = Mathf.Clamp(_liveShapeWeight, 0f, 1f);

				umaData.SetBlendShape(_blendshapeToApply, _liveShapeWeight);
			}

			#endregion
		}

		#endregion
	}
}
