using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.PoseTools;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace UMA
{
	[System.Serializable]
	public class BonePoseDNAConverterPlugin : DynamicDNAPlugin
	{

		[SerializeField]
		private List<BonePoseDNAConverter> _poseDNAConverters = new List<BonePoseDNAConverter>();

		public List<BonePoseDNAConverter> poseDNAConverters
		{
			get { return _poseDNAConverters; }
			set { _poseDNAConverters = value; }
		}

		#region BACKWARDS COMPATIBILITY 

		//the following are backwards compatible methods for DynamicDNAConverterBehaviour.StartingPose

		//backwards compatibility
		public UMABonePose StartingPose
		{
			get {
				/*if(_poseDNAConverters.Count > 0)
				{
					for(int i = 0; i < _poseDNAConverters.Count; i++)
					{
						if (_poseDNAConverters[i].poseToApply != null && (_poseDNAConverters[i].startingPoseWeight.defaultWeight == 1  > 0f)
							return _poseDNAConverters[i].poseToApply;
					}
				}*/
				return null;
			}
			//this is not bulletproof but it will be obsolete anyway
			set {
				/*if(_poseDNAConverters.Count > 0)
				{
					int foundIndex = -1;
					for(int i = 0; i < _poseDNAConverters.Count; i++)
					{
						if (_poseDNAConverters[i].poseToApply == value)
						{
							foundIndex = i;
							break;
						}
					}
					if(foundIndex >= 0 && foundIndex != 0)
					{
						//move the existing entry to 0
						var current = new BonePoseDNAConverter(_poseDNAConverters[foundIndex]);
						_poseDNAConverters.RemoveAt(foundIndex);
						_poseDNAConverters.Insert(0, current);
						return;
					}
				}
				_poseDNAConverters.Insert(0, new BonePoseDNAConverter(value, 1f));*/
			}
		}
		//backwards compatibility
		public float StartingPoseWeight
		{
			get
			{
				/*if (_poseDNAConverters.Count > 0)
				{
					for (int i = 0; i < _poseDNAConverters.Count; i++)
					{
						if (_poseDNAConverters[i].poseToApply != null && _poseDNAConverters[i].defaultPoseWeight > 0f)
							return _poseDNAConverters[i].defaultPoseWeight;
					}
				}*/
				return 0f;
			}
			set {
				/*if (_poseDNAConverters.Count > 0)
				{
					_poseDNAConverters[0].defaultPoseWeight = value;
				}*/
			}
		}

		#endregion

		#region REQUIRED DYNAMICDNAPLUGIN PROPERTIES

		public override string PluginHelp
		{
			get { return "Bone Pose DNA Converters convert the set dna names into weight settings for UMA Bone Pose that will be applied to a character. Normally you will set the 'Default Pose Weight' to 0 so that the pose is only applied when one of the set 'Modifier DNAs' returns a suitable value. However for a 'Starting Pose' you can set the 'Default Pose Weight' to 1. Optionally, you can link the 'Default Pose Weight' to a dna value on each character by setting up the 'Pose Weight DNA'"; }
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
				for (int i = 0; i < _poseDNAConverters.Count; i++)
				{
					for (int ci = 0; ci < _poseDNAConverters[i].UsedDNANames.Count; ci++)
					{
						if (!dict.ContainsKey(_poseDNAConverters[i].UsedDNANames[ci]))
							dict.Add(_poseDNAConverters[i].UsedDNANames[ci], new List<int>());

						dict[_poseDNAConverters[i].UsedDNANames[ci]].Add(i);
					}
				}
				return dict;
			}
		}

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			for(int i = 0; i < _poseDNAConverters.Count; i++)
			{
				_poseDNAConverters[i].ApplyDNA(umaData, skeleton, dnaTypeHash);
			}
		}

		#endregion

		#region INSPECTOR GUI OVERRIDES

#if UNITY_EDITOR
		/*public override string[] ImportSettingsMethods
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
		}*/
		public override string[] ImportSettingsMethods
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

		public override GUIContent GetPluginEntryLabel(SerializedProperty entry, SerializedObject pluginSO, int entryIndex)
		{
			if (_poseDNAConverters[entryIndex].poseToApply != null)
			{
				return new GUIContent(_poseDNAConverters[entryIndex].poseToApply.name);
			}
			return base.GetPluginEntryLabel(entry, pluginSO, entryIndex);
		}

		public override bool ImportSettings(UnityEngine.Object pluginToImport, int importMethod)
		{
			var importPlug = pluginToImport as BonePoseDNAConverterPlugin;
			if (importPlug == null)
			{
				Debug.LogWarning("The plugin you are trying to import was not a PoseDNAConverterPlugin!");
				return false;
			}
			if (importPlug._poseDNAConverters.Count == 0)
			{
				Debug.LogWarning("The plugin you are trying to import had no settings!");
				return false;
			}
			//Method Replace
			if (importMethod == 1)
			{
				_poseDNAConverters.Clear();
			}
			for (int i = 0; i < importPlug._poseDNAConverters.Count; i++)
			{
				_poseDNAConverters.Add(importPlug._poseDNAConverters[i]);
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			return true;
		}
		//I think this is just 'Add/Replace' now- maybe we can figure out some logic for Overwrite/AddOverwrite
		/*public override bool ImportSettings(UnityEngine.Object pluginToImport, int importMethod)
		{
			var importPlug = pluginToImport as BonePoseDNAConverterPlugin;
			if (importPlug == null)
			{
				Debug.LogWarning("The plugin you are trying to import was not a PoseDNAConverterPlugin!");
				return false;
			}
			if (importPlug._poseDNAConverters.Count == 0)
			{
				Debug.LogWarning("The plugin you are trying to import had no settings!");
				return false;
			}
			//Method Replace
			if (importMethod == 1)
			{
				_poseDNAConverters.Clear();
			}
			bool existed = false;
			UMABonePose ovrBP = null;
			DNAEvaluator ovrEval = null;
			for (int i = 0; i < importPlug._poseDNAConverters.Count; i++)
			{
				existed = false;
				ovrBP = null;
				//if method is Add check there is no existing
				//if method is Overwrite or AddOverwrite check if there is an existing entry with values we want to overwrite
				if (importMethod == 0 || importMethod == 2 || importMethod == 3)
				{
					ovrBP = importPlug._poseDNAConverters[i].poseToApply;
					ovrEval = importPlug._poseDNAConverters[i].startingPoseWeight.
					for (int ii = 0; ii < _poseDNAConverters.Count; ii++)
					{
						if (_poseDNAConverters[ii].poseToApply == ovrBP && _poseDNAConverters[ii].defaultPoseWeightDNA == ovrEval)//this will only import one modifier that is using this pose is that right?
						{
							if (importMethod == 2 || importMethod == 3)
							{
								_poseDNAConverters[ii].defaultPoseWeightDNA = new DNAEvaluator(importPlug._poseDNAConverters[i].defaultPoseWeightDNA);
								_poseDNAConverters[ii].defaultPoseWeight = importPlug._poseDNAConverters[i].defaultPoseWeight;
								_poseDNAConverters[ii].onMissingPoseDNA = importPlug._poseDNAConverters[i].onMissingPoseDNA;
								bool foundModifier = false;
								string importReducerDNA = "";
								for(int ri = 0; ri < importPlug._poseDNAConverters[i].modifierDnas.Count; ri++)
								{
									foundModifier = false;
									importReducerDNA = importPlug._poseDNAConverters[i].modifierDnas[ri].dnaName;
									if (!string.IsNullOrEmpty(importReducerDNA))
									{
										for (int rii = 0; rii < _poseDNAConverters[ii].modifierDnas.Count; rii++)
										{
											if (_poseDNAConverters[ii].modifierDnas[rii].dnaName == importReducerDNA)
											{
												foundModifier = true;
												_poseDNAConverters[ii].modifierDnas[rii].evaluator = new DNAEvaluationGraph( importPlug._poseDNAConverters[i].modifierDnas[ri].evaluator);
												_poseDNAConverters[ii].modifierDnas[rii].multiplier = importPlug._poseDNAConverters[i].modifierDnas[ri].multiplier;
											}
										}
										if (!foundModifier)
										{
											_poseDNAConverters[ii].modifierDnas.Add(new DNAEvaluator(importPlug._poseDNAConverters[i].modifierDnas[ri]));
										}
									}
								}
							}
							existed = true;
						}
					}
				}
				if (!existed && importMethod != 2)//if importmethod != overwrite
				{
					_poseDNAConverters.Add(new BonePoseDNAConverter(importPlug._poseDNAConverters[i]));
				}
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			return true;
		}*/

#endif
			#endregion

			#region SPECIAL TYPES

		[System.Serializable]
		public class BonePoseDNAConverter
		{
			public enum onMissingPoseDNAOpts { UseGlobalWeight, UseZero }

			#region FIELDS

			[Tooltip("The UMABonePose to apply to the character. This will effectively 'morph' the character into a different shape (using bone deformation so clothes will still fit).")]
			[SerializeField]
			private UMABonePose _poseToApply;

			[SerializeField]
			[Tooltip("Make the default weight 1 to apply the pose on start, 0 so the pose is only applied by 'Modifying DNA' below. Or you can hook the weight up to a dna on the character to control this dynamically.")]
			private DynamicDefaultWeight _startingPoseWeight = new DynamicDefaultWeight();

			/*
			//TODO DITCH THE FOLLOWING AND FIX METHODS
			[SerializeField]
			[Tooltip("The default weight for the pose when no dna is applied. Usually this is zero, but for a 'Starting Pose' set this value to 1. NOTE: Changing this value affects all characters that use the same converter behaviour. Set up a 'Default Pose Weight DNA'  (below) to affect the default weight 'per character'.")]
			[Range(0f, 1f)]
			[HideInInspector]
			public float _defaultPoseWeight = 0f;

			[SerializeField]
			[Tooltip("A DNA to use for setting the 'Default Pose Weight' (above)")]
			[DNAEvaluator.Config(true)]
			[HideInInspector]
			private DNAEvaluator _defaultPoseWeightDNA;

			[SerializeField]
			[HideInInspector]
			[Tooltip("If the 'Default Pose Weight DNA'  is assigned but not available, should the 'Default Pose Weight' be used or zero?")]
			private onMissingPoseDNAOpts _onMissingPoseDNA = onMissingPoseDNAOpts.UseGlobalWeight;
			*/

			[SerializeField]
			[Tooltip("Add dna(s) here that will change the amount that this Pose is applied depending on their evaluated value.")]
			private DNAEvaluatorList _modifyingDNA = new DNAEvaluatorList();

			#endregion

			#region PRIVATE VARS

			private float _livePoseWeight = 0f;

			private int _dnaIndex = -1;

			private DynamicUMADnaBase _activeDNA;

			#endregion

			#region PUBLIC PROPERTIES

			public UMABonePose poseToApply
			{
				get { return _poseToApply; }
				set { _poseToApply = value; }
			}

			public DynamicDefaultWeight startingPoseWeight
			{
				get { return _startingPoseWeight; }
				set { _startingPoseWeight = value; }
			}

			/*public float defaultPoseWeight
			{
				get { return _defaultPoseWeight; }
				set { _defaultPoseWeight = value; }
			}

			public onMissingPoseDNAOpts onMissingPoseDNA
			{
				get { return _onMissingPoseDNA; }
				set { _onMissingPoseDNA = value; }
			}

			public DNAEvaluator defaultPoseWeightDNA
			{
				get { return _defaultPoseWeightDNA; }
				set { _defaultPoseWeightDNA = value; }
			}*/

			public DNAEvaluatorList modifierDnas
			{
				get { return _modifyingDNA; }
				set { _modifyingDNA = new DNAEvaluatorList(value); }
			}

			/*
			//The following properties are handy for Timeline

			/// <summary>
			/// Gets/Sets the dnaName that the defaultPoseWeightDNA evaluator uses. If no dnaEvaluator has been assigned to the 'defaultPoseWeightDNA' field one will be created using the 'default' evaluation settings.
			/// You can use this to temporarily change the dna that is affecting the pose weight for special effects
			/// </summary>
			public string DefaultPoseWeightDNAName
			{
				get
				{
					if (_defaultPoseWeightDNA != null)
						return _defaultPoseWeightDNA.dnaName;
					else
						return "";
				}
				set
				{
					if (_defaultPoseWeightDNA == null)
						_defaultPoseWeightDNA = new DNAEvaluator(value, DNAEvaluationGraph.Default, 1f);
					else
						_defaultPoseWeightDNA.dnaName = value;
				}
			}

			/// <summary>
			/// Gets/Sets the multiplier that the defaultPoseWeightDNA evaluator uses. 
			/// You can use this to turn the pose weight up or down independently of dna value for special effects
			/// </summary>
			public float DefaultPoseWeightDNAMultiplier
			{
				get
				{
					if (_defaultPoseWeightDNA != null)
						return _defaultPoseWeightDNA.multiplier;
					else
						return 0f;
				}
				set
				{
					if (_defaultPoseWeightDNA != null)
						_defaultPoseWeightDNA.multiplier = value;
				}
			}*/

			#endregion

			#region CONSTRUCTOR

			public BonePoseDNAConverter() { }

			public BonePoseDNAConverter(UMABonePose poseToApply, float startingPoseWeight = 0f, DNAEvaluator startingPoseWeightDNA = null)
			{
				this._poseToApply = poseToApply;
				//this._defaultPoseWeight = defaultPoseWeight;
				//this._defaultPoseWeightDNA = new DNAEvaluator(defaultPoseWeightDNA);
				this._startingPoseWeight = new DynamicDefaultWeight(startingPoseWeight, startingPoseWeightDNA.dnaName, startingPoseWeightDNA.evaluator, startingPoseWeightDNA.multiplier);
			}

			public BonePoseDNAConverter(BonePoseDNAConverter other)
			{
				this._poseToApply = other.poseToApply;
				//this._defaultPoseWeight = other.defaultPoseWeight;
				//this._defaultPoseWeightDNA = new DNAEvaluator(other.defaultPoseWeightDNA);
				//this.onMissingPoseDNA = other.onMissingPoseDNA;
				this._startingPoseWeight = other._startingPoseWeight;
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
					//if (!String.IsNullOrEmpty(_defaultPoseWeightDNA.dnaName))
					//	usedNames.Add(_defaultPoseWeightDNA.dnaName);
					if(!String.IsNullOrEmpty(_startingPoseWeight.dnaName))
						usedNames.Add(_startingPoseWeight.dnaName);
					for (int i = 0; i < _modifyingDNA.Count; i++)
					{
						if (!String.IsNullOrEmpty(_modifyingDNA[i].dnaName))
							usedNames.Add(_modifyingDNA[i].dnaName);
					}
					return usedNames;
				}
			}

			public void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
			{
				/* = _defaultPoseWeight;
				//we can cast to this because the dna field in the converter wont accept anything else
				_activeDNA = (DynamicUMADnaBase)umaData.GetDna(dnaTypeHash);

				if (!string.IsNullOrEmpty(_defaultPoseWeightDNA.dnaName))
				{
					_dnaIndex = System.Array.IndexOf(_activeDNA.Names, _defaultPoseWeightDNA.dnaName);
					if (_dnaIndex > -1)
					{
						_livePoseWeight = _defaultPoseWeightDNA.Evaluate(_activeDNA.GetValue(_dnaIndex));
					}
					else
					{
						//dna not found obey the _onMissingPoseDNA option
						if (_onMissingPoseDNA == onMissingPoseDNAOpts.UseZero)
							_livePoseWeight = 0f;
					}
				}*/
				//we can cast to this because the dna field in the converter wont accept anything else
				_activeDNA = (DynamicUMADnaBase)umaData.GetDna(dnaTypeHash);
				_livePoseWeight = _startingPoseWeight.GetWeight(_activeDNA);

				_livePoseWeight += _modifyingDNA.Evaluate(_activeDNA);
				if (_livePoseWeight < 0f)
					_livePoseWeight = 0f;

				_poseToApply.ApplyPose(skeleton, _livePoseWeight);
			}

			#endregion
		}

		#endregion
	}
}
