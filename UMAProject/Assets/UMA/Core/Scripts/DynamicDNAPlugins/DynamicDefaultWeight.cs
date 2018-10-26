using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	//This is used by DynamicDNAPlugins to control their overall impact on the character. by turning the default weight to zero you are effectively
	//disabling the converter. But this class also allows you to hook up a dna so that weight can be controlled 'per character'
	//Some individual plugins also use this to provide more control, for example the BonePose plugin uses it to determine if a given pose is
	//is a 'Starting Pose' (i.e. how much the pose should be applied on start regardless of dna)
	[System.Serializable]
	public class DynamicDefaultWeight
	{
		public enum MissingDNAForWeightOpts { UseDefaultWeight, EvaluateOne, EvaluateZeroPointFive, EvaluateZero };

		[Tooltip("The default weight- applies to all characters that use the converter this plugin resides in. Override this with DNAForWeight for 'per character' control")]
		[Range(0f, 1f)]
		[SerializeField]
		protected float _defaultWeight = 0f;
		[Tooltip("If set, the weight value will be controlled by the given dna on the character.")]
		[SerializeField]
		[DNAEvaluator.Config(true, true, true)]
		protected DNAEvaluator _DNAForWeight = new DNAEvaluator("", DNAEvaluationGraph.Raw, 1);
		[Tooltip("If 'DNA for Weight' was set but the dna name is missing on the current character, use the default weight or evaluate a fake value?")]
		[SerializeField]
		protected MissingDNAForWeightOpts _onMissingDNA = MissingDNAForWeightOpts.UseDefaultWeight;

		public float defaultWeight
		{
			get { return _defaultWeight; }
			set { _defaultWeight = value; }
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

		public DynamicDefaultWeight() { }

		public DynamicDefaultWeight(DynamicDefaultWeight other)
		{
			_defaultWeight = other._defaultWeight;
			_DNAForWeight = other._DNAForWeight;
			_onMissingDNA = other._onMissingDNA;
		}

		public DynamicDefaultWeight(float defaultWeight = 0f, string dnaForWeightName = "", DNAEvaluationGraph dnaForWeightGraph = null, float dnaForWeightMultiplier = 1f, MissingDNAForWeightOpts onMissingDNA = MissingDNAForWeightOpts.UseDefaultWeight)
		{
			_defaultWeight = defaultWeight;
			if (!string.IsNullOrEmpty(dnaForWeightName))
			{
				_DNAForWeight = new DNAEvaluator(dnaForWeightName, dnaForWeightGraph, dnaForWeightMultiplier);
			}
			else
			{
				_DNAForWeight = new DNAEvaluator("", DNAEvaluationGraph.Raw, 1);
			}
			_onMissingDNA = onMissingDNA;
		}

		public float GetWeight(UMADnaBase umaDna = null)
		{
			if (!string.IsNullOrEmpty(_DNAForWeight.dnaName))
			{
				if (umaDna != null && System.Array.IndexOf(umaDna.Names, _DNAForWeight.dnaName) > -1)
					return _DNAForWeight.Evaluate(umaDna);
				else if (_onMissingDNA == MissingDNAForWeightOpts.EvaluateOne)
					return _DNAForWeight.Evaluate(1f);
				else if (_onMissingDNA == MissingDNAForWeightOpts.EvaluateZeroPointFive)
					return _DNAForWeight.Evaluate(0.5f);
				else if (_onMissingDNA == MissingDNAForWeightOpts.EvaluateZero)
					return _DNAForWeight.Evaluate(0f);
				else
					return _defaultWeight;
			}
			else
				return _defaultWeight;
		}
	}
}
