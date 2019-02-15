using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
namespace UMA
{
	[System.Serializable]
	public class OverallScaleDNAConverterPlugin : DynamicDNAPlugin
	{

		#region FIELDS

		[SerializeField]
		private List<OverallScaleModifier> _overallScaleModifiers = new List<OverallScaleModifier>();

		#endregion

		#region PUBLIC PROPERTIES

		public List<OverallScaleModifier> overallScaleModifiers
		{
			get { return _overallScaleModifiers; }
			set { _overallScaleModifiers = value; }
		}

		#endregion

		#region REQUIRED DYNAMICDNAPLUGIN METHODS PROPERTIES

		/// <summary>
		/// Returns a dictionary of all the dna names in use by the plugin and the entries in its converter list that reference them
		/// </summary>
		/// <returns></returns>
		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				var dict = new Dictionary<string, List<int>>();
				for (int i = 0; i < _overallScaleModifiers.Count; i++)
				{
					for (int ci = 0; ci < _overallScaleModifiers[i].UsedDNANames.Count; ci++)
					{
						if (!dict.ContainsKey(_overallScaleModifiers[i].UsedDNANames[ci]))
							dict.Add(_overallScaleModifiers[i].UsedDNANames[ci], new List<int>());

						dict[_overallScaleModifiers[i].UsedDNANames[ci]].Add(i);
					}
				}
				return dict;
			}
		}
		/// <summary>
		/// Apply the overall scale modifications according to the given dna (determined by the dnaTypeHash)
		/// </summary>
		/// <param name="umaData"></param>
		/// <param name="skeleton"></param>
		/// <param name="dnaTypeHash"></param>
		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			if (this.converterController == null /*|| this.converterController.converterBehaviour == null*/ || _overallScaleModifiers.Count == 0)
				return;
			var umaDna = (DynamicUMADnaBase)umaData.GetDna(dnaTypeHash);
			//master weight determines how much we modify the converters base scale to our new value, 1 its fully overridden, 0 its left as it is
			var masterWeightCalc = masterWeight.GetWeight(umaDna);
			if (masterWeightCalc == 0f)
				return;

			float baseScale = this.converterController.baseScale;

			//Each modifier wants to change the base scale to its overall scale value depending on how stronly its dna(s) are applied
			//so we need to accumulate the differences each one wants to make rather than the full value
			float evaluatedScale = 0f;
			float evaluatedDiff = 0f;
			for (int i = 0; i < _overallScaleModifiers.Count; i++)
			{
				evaluatedDiff += ((_overallScaleModifiers[i].overallScale - baseScale) * _overallScaleModifiers[i].GetEvaluatedDNA(umaDna));
			}
			//add the combined differences to the base scale
			evaluatedScale = baseScale + evaluatedDiff;
			//lerp to that result based on the masterWeightCalc
			float newScale = Mathf.Lerp(baseScale, evaluatedScale, masterWeightCalc);
			this.converterController.liveScale = newScale;
		}

		#endregion

		#region DYNAMICDNAPLUGIN EDITOR OVERRIDES

#if UNITY_EDITOR

		public override string PluginHelp
		{
			get { return "Changes the overall scale value on this plugins converter behaviour based on dna. Each entry will be evaluated according to evaluated weight of its dna entry and the weigted avaerage result of all the entries will be sent to the converter behaviour to use for its 'overall scale' calculation"; }
		}

		public override void OnAddEntryCallback(SerializedObject pluginSO, int entryIndex)
		{
			var thismodifier = pluginSO.FindProperty("_overallScaleModifiers").GetArrayElementAtIndex(entryIndex);
			if (thismodifier.FindPropertyRelative("_overallScale").floatValue == 0f)
				thismodifier.FindPropertyRelative("_overallScale").floatValue = 0.88f;//the default value for humanMale as defined in OverallScaleModifier
		}
#endif
		#endregion

		#region SPECIAL TYPES

		[System.Serializable]
		public class OverallScaleModifier
		{
			//Just to help with organising in the inspector
			[SerializeField]
			[Tooltip("This is just a label for helping organise entries in the UI")]
			private string _label;
			[SerializeField]
			[Tooltip("If no modifying dna is specified below this scale will be fully applied to the character.")]
			private float _overallScale = 0.88f;
			[SerializeField]
			[Tooltip("Modify how much the overallScale above is applied to the character based on dna value(s) you specify here")]
			private DNAEvaluatorList _modifyingDNA = new DNAEvaluatorList();

			public float overallScale
			{
				get { return _overallScale; }
			}

			public List<string> UsedDNANames
			{
				get
				{
					var usedNames = new List<string>();
					for (int i = 0; i < _modifyingDNA.Count; i++)
					{
						if (!string.IsNullOrEmpty(_modifyingDNA[i].dnaName))
							usedNames.Add(_modifyingDNA[i].dnaName);
					}
					return usedNames;
				}
			}

			public float GetEvaluatedDNA(UMADnaBase umaDNA)
			{
				if (_modifyingDNA.Count > 0)
					return _modifyingDNA.Evaluate(umaDNA);
				return 1f;//if there is no modifying dna assume the overall scale is fully applied
			}

		}

		#endregion
	}
}
