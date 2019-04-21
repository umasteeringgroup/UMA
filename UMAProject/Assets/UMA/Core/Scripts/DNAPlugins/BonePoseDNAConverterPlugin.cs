using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.PoseTools;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UMA.CharacterSystem;

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
		//TODO can we make this work again?

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

		#region DYNAMICDNAPLUGIN PROPERTIES

#if UNITY_EDITOR
		public override string PluginHelp
		{
			get { return "Bone Pose DNA Converters convert the set dna names into weight settings for UMA Bone Pose that will be applied to a character. You can use the 'Starting Pose Weight' to force this pose on all characters that use this converter at the start. Or you can hook up to a modifying dna so that the pose is only applied based on a characters dna value."; }
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
		/// <summary>
		/// Apply the boneposes according to the given dna (determined by the dnaTypeHash)
		/// </summary>
		/// <param name="umaData"></param>
		/// <param name="skeleton"></param>
		/// <param name="dnaTypeHash"></param>
		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			var umaDna = umaData.GetDna(dnaTypeHash);
			var masterWeightCalc = masterWeight.GetWeight(umaDna);
			for (int i = 0; i < _poseDNAConverters.Count; i++)
			{
				_poseDNAConverters[i].ApplyDNA(umaData, skeleton, dnaTypeHash, masterWeightCalc);
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
			//deal with legacy settings from a DynamicDNAConverterBehaviour prefab
			if (typeof(GameObject).IsAssignableFrom(pluginToImport.GetType()))
			{
				var DDCB = (pluginToImport as GameObject).GetComponent<DynamicDNAConverterBehaviour>();
				if (DDCB != null)
				{
					return ImportLegacySettings(DDCB, importMethod);
				}
			}
			//TODO Deal with Morphset?
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
				_poseDNAConverters.Add(new BonePoseDNAConverter(importPlug._poseDNAConverters[i]));
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			return true;
		}

#pragma warning disable 618
		private bool ImportLegacySettings(DynamicDNAConverterBehaviour DDCB, int importMethod)
		{
			if(importMethod == 1)
				_poseDNAConverters.Clear();
			if(DDCB.startingPose != null)
				_poseDNAConverters.Add(new BonePoseDNAConverter(DDCB.startingPose, DDCB.startingPoseWeight));
			return false;
		}
#pragma warning restore 618

#endif
		#endregion

		#region SPECIAL TYPES

		[System.Serializable]
		public class BonePoseDNAConverter
		{

			#region FIELDS

			[Tooltip("The UMABonePose to apply to the character. This will effectively 'morph' the character into a different shape (using bone deformation so clothes will still fit).")]
			[SerializeField]
			private UMABonePose _poseToApply;

			[SerializeField]
			[Tooltip("Make the default weight 1 to apply the pose on start to *all* characters that use this converter or set to 0 so the pose is only applied by 'Modifying DNA' below. If you want to affect this 'per character' use 'Modifying DNA' instead")]
			[Range(0f, 1f)]
			private float _startingPoseWeight = 0f;

			[SerializeField]
			[Tooltip("Add dna(s) here that will change the amount that this Pose is applied depending on their evaluated value.")]
			private DNAEvaluatorList _modifyingDNA = new DNAEvaluatorList();

			#endregion

			#region PRIVATE VARS

			private float _livePoseWeight = 0f;

			//private int _dnaIndex = -1;

			private DynamicUMADnaBase _activeDNA;

			#endregion

			#region PUBLIC PROPERTIES

			public UMABonePose poseToApply
			{
				get { return _poseToApply; }
				set { _poseToApply = value; }
			}

			public float startingPoseWeight
			{
				get { return _startingPoseWeight; }
				set { _startingPoseWeight = value; }
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

			public BonePoseDNAConverter() { }

			public BonePoseDNAConverter(UMABonePose poseToApply, float startingPoseWeight, DNAEvaluatorList modifyingDnas)
			{
				this._poseToApply = poseToApply;
				this._startingPoseWeight = startingPoseWeight;
				if (modifyingDnas != null)
					this._modifyingDNA = new DNAEvaluatorList(modifyingDnas);
			}

			public BonePoseDNAConverter(UMABonePose poseToApply, float startingPoseWeight = 0f, List<DNAEvaluator> modifyingDnas = null)
			{
				this._poseToApply = poseToApply;
				this._startingPoseWeight = startingPoseWeight;
				if(modifyingDnas != null)
					this._modifyingDNA = new DNAEvaluatorList(modifyingDnas);
			}

			public BonePoseDNAConverter(BonePoseDNAConverter other)
			{
				this._poseToApply = other._poseToApply;
				this.startingPoseWeight = other._startingPoseWeight;
				this._modifyingDNA = new DNAEvaluatorList(other._modifyingDNA);
			}

			#endregion

			#region METHODS

			public void ApplyDNA(UMAData umaData, UMASkeleton skeleton, UMADnaBase activeDNA, float masterWeight = 1f)
			{
				if (_poseToApply == null)
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning(umaData.gameObject.name + " had an invalid or empty pose set in its BonePoseDNAConverters in its DNAConverterController");
					return;
				}
				_livePoseWeight = _startingPoseWeight;

				//dna weight superceeds startingWeight if it exists
				if (_modifyingDNA.UsedDNANames.Count > 0)
				{
					_livePoseWeight = _modifyingDNA.Evaluate(activeDNA);
				}
				_livePoseWeight = _livePoseWeight * masterWeight;
				_livePoseWeight = Mathf.Clamp(_livePoseWeight, 0f, 1f);

				_poseToApply.ApplyPose(skeleton, _livePoseWeight);
			}

			public void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash, float masterWeight = 1f)
			{
				if (_poseToApply == null)
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning(umaData.gameObject.name + " had an invalid or empty pose set in its BonePoseDNAConverters in its DNAConverterController");
					return;
				}
				_livePoseWeight = _startingPoseWeight;

				//dna weight superceeds startingWeight if it exists
				if (_modifyingDNA.UsedDNANames.Count > 0)
				{
					_activeDNA = (DynamicUMADnaBase)umaData.GetDna(dnaTypeHash);
					_livePoseWeight = _modifyingDNA.Evaluate(_activeDNA);
				}
				_livePoseWeight = _livePoseWeight * masterWeight;
				_livePoseWeight = Mathf.Clamp(_livePoseWeight, 0f, 1f);

				_poseToApply.ApplyPose(skeleton, _livePoseWeight);
			}

			#endregion
		}

		#endregion
	}
}
