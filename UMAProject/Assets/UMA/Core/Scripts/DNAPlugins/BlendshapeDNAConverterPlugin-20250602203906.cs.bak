using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

		#region DYNAMICDNAPLUGIN PROPERTIES

#if UNITY_EDITOR
		public override string PluginHelp
		{
			get { return "Blendshape DNA Converters convert the set dna names into weight settings for Blendshape on the character. You can use the 'Starting Shape Weight' to force this shape on for all characters that use this converter at the start. Or you can hook up to a modifying dna so that the shape is only applied based on a characters dna value."; }
		}
#endif

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
                        {
                            dict.Add(_blendshapeDNAConverters[i].UsedDNANames[ci], new List<int>());
                        }

                        dict[_blendshapeDNAConverters[i].UsedDNANames[ci]].Add(i);
					}
				}
				return dict;
			}
		}

		public override ApplyPassOpts ApplyPass
		{
			get
			{
				return ApplyPassOpts.PostPass;
			}
		}

		/// <summary>
		/// Apply the blendshape modifications according to the given dna (determined by the dnaTypeHash)
		/// </summary>
		/// <param name="umaData"></param>
		/// <param name="skeleton"></param>
		/// <param name="dnaTypeHash"></param>
		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			var umaDna = umaData.GetDna(dnaTypeHash);
			var masterWeightCalc = masterWeight.GetWeight(umaDna);
			for (int i = 0; i < _blendshapeDNAConverters.Count; i++)
			{
				_blendshapeDNAConverters[i].ApplyDNA(umaData, skeleton, umaDna, masterWeightCalc);
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
				"Replace"
				};
			}
		}

		public override bool ImportSettings(UnityEngine.Object pluginToImport, int importMethod)
		{
			//TODO Deal with Morphset?
			var importPlug = pluginToImport as BlendshapeDNAConverterPlugin;
			if (importPlug == null)
			{
				Debug.LogWarning("The plugin you are trying to import was not a PoseDNAConverterPlugin!");
				return false;
			}
			if (importPlug._blendshapeDNAConverters.Count == 0)
			{
				Debug.LogWarning("The plugin you are trying to import had no settings!");
				return false;
			}
			//Method Replace
			if (importMethod == 1)
			{
				_blendshapeDNAConverters.Clear();
			}
			for (int i = 0; i < importPlug._blendshapeDNAConverters.Count; i++)
			{
				_blendshapeDNAConverters.Add(new BlendshapeDNAConverter(importPlug._blendshapeDNAConverters[i]));
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

			#region FIELDS

			[Tooltip("The Blendshape to apply to the character.")]
			[SerializeField]
			private string _blendshapeToApply;

			[SerializeField]
			[Tooltip("Make the default weight 1 to apply the blendshape on start to *all* characters that use this converter or set to 0 so the shape is only applied by 'Modifying DNA' below. If you want to affect this 'per character' use 'Modifying DNA' instead")]
			[Range(0f, 1f)]
			private float _startingShapeWeight = 0f;

			[SerializeField]
			[Tooltip("Add dna(s) here that will change the amount that this blendshape is applied depending on their evaluated value.")]
			private DNAEvaluatorList _modifyingDNA = new DNAEvaluatorList();

			#endregion

			#region PRIVATE VARS

			private float _liveShapeWeight = 0f;

			private DynamicUMADnaBase _activeDNA;

			#endregion

			#region PUBLIC PROPERTIES

			public string blendshapeToApply
			{
				get { return _blendshapeToApply; }
				set { _blendshapeToApply = value; }
			}

			public float startingShapeWeight
			{
				get { return _startingShapeWeight; }
				set { _startingShapeWeight = value; }
			}

			public DNAEvaluatorList modifyingDNA
			{
				get { return _modifyingDNA; }
				set { _modifyingDNA = new DNAEvaluatorList(value); }
			}

			//TODO Timeline properties for screwing with the modifying dnas?

			public List<string> UsedDNANames
			{
				get
				{
					//Do we include masterDNA in this? 
					//It will make a dna entry where every entry in any plugin that uses the name shows up
					//could be more annoying/confusing than useful
					return _modifyingDNA.UsedDNANames;
				}
			}

			#endregion

			#region CONSTRUCTOR

			public BlendshapeDNAConverter() { }

			public BlendshapeDNAConverter(string blendshapeToApply, float startingShapeWeight, DNAEvaluatorList modifyingDnas)
			{
				this._blendshapeToApply = blendshapeToApply;
				this._startingShapeWeight = startingShapeWeight;
				if(modifyingDnas != null)
                {
                    this._modifyingDNA = new DNAEvaluatorList(modifyingDnas);
                }
            }

			public BlendshapeDNAConverter(string shapeToApply, float startingShapeWeight = 0f, List<DNAEvaluator> modifyingDnas = null)
			{
				this._blendshapeToApply = shapeToApply;
				this._startingShapeWeight = startingShapeWeight;
				if(modifyingDnas != null)
                {
                    this._modifyingDNA = new DNAEvaluatorList(modifyingDnas);
                }
            }

			public BlendshapeDNAConverter(BlendshapeDNAConverter other)
			{
				this._blendshapeToApply = other._blendshapeToApply;
				this._startingShapeWeight = other._startingShapeWeight;
				this._modifyingDNA = new DNAEvaluatorList(other._modifyingDNA);
			}

			#endregion

			#region METHODS

			//TODO methods for screwing with the reducer dnas? Better if we can use fancy properties with indexers

			public void ApplyDNA(UMAData umaData, UMASkeleton skeleton, UMADnaBase activeDNA, float masterWeight = 1f)
			{
				_liveShapeWeight = _startingShapeWeight;

				//dna weight superceeds startingWeight if it exists
				if (_modifyingDNA.UsedDNANames.Count > 0)
				{
					_liveShapeWeight = _modifyingDNA.Evaluate(activeDNA);
				}
				_liveShapeWeight = _liveShapeWeight * masterWeight;

				//In Unity 2018.3+ blendshapes can have negative values too, so in that case allow the value to go to -1
#if !UNITY_2018_3_OR_NEWER
				_liveShapeWeight = Mathf.Clamp(_liveShapeWeight, 0f, 1f);
#endif
				umaData.SetBlendShape(_blendshapeToApply, _liveShapeWeight);
			}

			public void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash, float masterWeight = 1f)
			{
				_liveShapeWeight = _startingShapeWeight;

				//dna weight superceeds startingWeight if it exists
				if (_modifyingDNA.UsedDNANames.Count > 0)
				{
					_activeDNA = (DynamicUMADnaBase)umaData.GetDna(dnaTypeHash);
					_liveShapeWeight = _modifyingDNA.Evaluate(_activeDNA);
				}
				_liveShapeWeight = _liveShapeWeight * masterWeight;
				_liveShapeWeight = Mathf.Clamp(_liveShapeWeight, 0f, 1f);

				umaData.SetBlendShape(_blendshapeToApply, _liveShapeWeight);
			}

#endregion
		}

#endregion
	}
}
