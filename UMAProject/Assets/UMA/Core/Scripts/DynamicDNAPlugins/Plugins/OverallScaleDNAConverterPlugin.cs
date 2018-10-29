using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace UMA
{
	[System.Serializable]
	public class OverallScaleDNAConverterPlugin : DynamicDNAPlugin
	{
		[System.Serializable]
		public class OverallScaleModifier
		{
			//Just to help with organising in the inspector
			[SerializeField]
			private string _label;
			[SerializeField]
			private float _overallScale = 0.88f;
			//[SerializeField]
			//private DynamicDefaultWeight _overallScaleWeight = new DynamicDefaultWeight();
			[SerializeField]
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
				return 1f;
			}

			public float GetEvaluatedScale(UMADnaBase umaDNA)
			{
				//return _overallScale * _overallScaleWeight.GetWeight(umaDNA);
				if (_modifyingDNA.Count > 0)
					return _overallScale * _modifyingDNA.Evaluate(umaDNA);
				return _overallScale;
			}
		}

		[SerializeField]
		private List<OverallScaleModifier> _overallScaleModifiers = new List<OverallScaleModifier>();

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

		public override string PluginHelp
		{
			get { return "Changes the overall scale value on this plugins converter behaviour based on dna. Each entry will be evaluated according to evaluated weight of its dna entry and the weigted avaerage result of all the entries will be sent to the converter behaviour to use for its 'overall scale' calculation"; }
		}

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			if (this.converterController == null || this.converterController.converterBehaviour == null || _overallScaleModifiers.Count == 0)
				return;
			var umaDna = (DynamicUMADnaBase)umaData.GetDna(dnaTypeHash);
			//master weight determines how much we modify the converters base scale to our new value, 1 its fully overridden, 0 its left as it is
			var masterWeightCalc = masterWeight.GetWeight(umaDna);
			if (masterWeightCalc == 0f)
				return;
			float evaluatedScale = 0f;
			float accumulatedWeight = 0f;
			float weight;
			for(int i = 0; i < _overallScaleModifiers.Count; i++)
			{
				weight = _overallScaleModifiers[i].GetEvaluatedDNA(umaDna);
				evaluatedScale += _overallScaleModifiers[i].overallScale * weight;
				accumulatedWeight += weight;
			}
			//if accumulatedWeight is greater than 1 bring the evaluatedScale back into a sane rage
			if(accumulatedWeight > 1)
				evaluatedScale = evaluatedScale * (1 / accumulatedWeight);			
			//float baseScale = this.converterAsset.converterBehaviour.overallScaleCalc;
			//float newScale = Mathf.Lerp(baseScale, evaluatedScale, masterWeightCalc);
			//this.converterAsset.converterBehaviour.overallScaleCalc = newScale;

			float baseScale2 = this.converterController.converterBehaviour.baseScale;
			float newScale2 = Mathf.Lerp(baseScale2, evaluatedScale, masterWeightCalc);
			this.converterController.converterBehaviour.liveScale = newScale2;
		}

	}
}
