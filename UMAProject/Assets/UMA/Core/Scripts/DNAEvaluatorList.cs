using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	//A dna evaluator list evaluates multiple dna at the same time according to each entrys evaluation graph.
	//the results are then combined together according to the 'aggregationMethod' option.
	//This provides huge flexibility when determining how different dna values interract with each other when they are affecting
	//a blendshape or bone pose for example.
	//You can generally treat this like a list of evaluators, but you can call Evaluate on it directly to get the aggregated result
	[System.Serializable]
	public class DNAEvaluatorList
	{
		public enum AggregationMethodOpts
		{
			Average,
			Cumulative,
			Minimum,
			Maximum
		};

		[SerializeField]
		private List<DNAEvaluator> _dnaEvaluators = new List<DNAEvaluator>();

		[SerializeField]
		[Tooltip("How the evaluated results of each entry are combined and returned. When 'Cumulative' is selected you can choose how each line will be combined with the preceeding one.")]
		private AggregationMethodOpts _aggregationMethod = AggregationMethodOpts.Average;

		public AggregationMethodOpts aggregationMethod
		{
			get { return _aggregationMethod; }
			set { _aggregationMethod = value; }
		}

		/// <summary>
		/// Returns a list of used dna names- any entries that dont have a name assigned are discarded. Beware that the list will include names that may not be in the current dna and these will return 0.5f
		/// </summary>
		public List<string> UsedDNANames
		{
			get
			{
				var ret = new List<string>();
				for(int i = 0; i < _dnaEvaluators.Count; i++)
				{
					if (!string.IsNullOrEmpty(_dnaEvaluators[i].dnaName))
						ret.Add(_dnaEvaluators[i].dnaName);
				}
				return ret;
			}
		}

		#region CONSTRUCTOR

		public DNAEvaluatorList()
		{
		}

		public DNAEvaluatorList(DNAEvaluatorList other)
		{
			_aggregationMethod = other._aggregationMethod;
			for(int i = 0; i < other._dnaEvaluators.Count; i++)
			{
				_dnaEvaluators.Add(new DNAEvaluator(other._dnaEvaluators[i]));
			}
		}

		public DNAEvaluatorList(List<DNAEvaluator> evaluators, AggregationMethodOpts aggregationMethod = AggregationMethodOpts.Average)
		{
			_aggregationMethod = aggregationMethod;
			for (int i = 0; i < evaluators.Count; i++)
			{
				_dnaEvaluators.Add(new DNAEvaluator(evaluators[i]));
			}
		}

		public DNAEvaluatorList(AggregationMethodOpts aggregationMethod)
		{
			_aggregationMethod = aggregationMethod;
		}

		#endregion

		#region METHODS

		/// <summary>
		/// For each evaluator in the list, finds the set dna name in the supplied dna list and applies the evaluator and multiplier to it. The resulting values are then aggregated together using the method defined in the 'AggregationMethod' field
		/// </summary>
		/// <param name="dna">The dna to search</param>
		/// <returns>The evaluated value</returns>
		public float Evaluate(UMADnaBase dna)
		{
			if(_dnaEvaluators.Count > 0)
			{
				return GetAggregateValueNew(dna);
			}
			else
			{
				return DNAEvaluator.defaultDNAValue;
			}
		}

		public float ApplyDNAToValue(UMADnaBase umaDna, float startingValue)
		{
			if (_dnaEvaluators.Count > 0)
			{
				return GetAggregateValueNew(umaDna, startingValue);
			}
			else
				return startingValue;
		}

		private float GetAggregateValueNew(UMADnaBase dna, float result = 0f)
		{
			float tempResult = 0f;
			if (_aggregationMethod == AggregationMethodOpts.Average)
			{
				var aveCount = result != 0f ? 1 : 0;
				for (int i = 0; i < _dnaEvaluators.Count; i++)
				{
					if (!string.IsNullOrEmpty(_dnaEvaluators[i].dnaName))
					{
						aveCount++;
						result += _dnaEvaluators[i].Evaluate(dna);
					}
				}
				if (aveCount > 0)
					result = result / aveCount;
			}
			else if (_aggregationMethod == AggregationMethodOpts.Maximum)
			{
				for (int i = 0; i < _dnaEvaluators.Count; i++)
				{
					tempResult = 0f;
					if (!string.IsNullOrEmpty(_dnaEvaluators[i].dnaName))
					{
						tempResult = _dnaEvaluators[i].Evaluate(dna);
						if (tempResult > result)
							result = tempResult;
					}
				}
			}
			else if (_aggregationMethod == AggregationMethodOpts.Minimum)
			{
				for (int i = 0; i < _dnaEvaluators.Count; i++)
				{
					tempResult = 0f;
					if (!string.IsNullOrEmpty(_dnaEvaluators[i].dnaName))
					{
						tempResult = _dnaEvaluators[i].Evaluate(dna);
						if (tempResult < result)
							result = tempResult;
					}
				}
			}
			else if (aggregationMethod == AggregationMethodOpts.Cumulative)
			{
				for (int i = 0; i < _dnaEvaluators.Count; i++)
				{
					tempResult = 0f;
					if (!string.IsNullOrEmpty(_dnaEvaluators[i].dnaName))
					{
						tempResult = _dnaEvaluators[i].Evaluate(dna);
						if (_dnaEvaluators[i].calcOption == DNAEvaluator.CalcOption.Add)
							result += tempResult;
						else if (_dnaEvaluators[i].calcOption == DNAEvaluator.CalcOption.Subtract)
							result -= tempResult;
						else if (_dnaEvaluators[i].calcOption == DNAEvaluator.CalcOption.Multiply)
							result *= tempResult;
						else if (_dnaEvaluators[i].calcOption == DNAEvaluator.CalcOption.Divide && tempResult != 0)
							result /= tempResult;
					}
				}
			}
			return result;
		}
		private float GetAggregateValue(List<float> vals)
		{
			float result = 0f;
			if(_aggregationMethod == AggregationMethodOpts.Average)
			{
				if (vals.Count > 0)
				{
					for (int i = 0; i < vals.Count; i++)
					{
						result += vals[i];
					}
					result = result / vals.Count;
				}
			}
			if(_aggregationMethod == AggregationMethodOpts.Cumulative)
			{
				for (int i = 0; i < vals.Count; i++)
				{
					result += vals[i];
				}
			}
			if(_aggregationMethod == AggregationMethodOpts.Minimum)
			{
				if (vals.Count > 0)
				{
					result = vals[0];
					for (int i = 0; i < vals.Count; i++)
					{
						if (vals[i] < result)
							result = vals[i];
					}
				}
			}
			if(_aggregationMethod == AggregationMethodOpts.Maximum)
			{
				if (vals.Count > 0)
				{
					result = vals[0];
					for (int i = 0; i < vals.Count; i++)
					{
						if (vals[i] > result)
							result = vals[i];
					}
				}
			}
			return result;
		}

		#endregion

		#region PASSTHRU LIST METHODS

		public DNAEvaluator this[int key]
		{
			get { return _dnaEvaluators[key]; }
			set { _dnaEvaluators[key] = value; }
		}

		public int Count
		{
			get { return _dnaEvaluators.Count; }
		}

		public void Add(DNAEvaluator evaluator)
		{
			_dnaEvaluators.Add(evaluator);
		}

		public void AddRange(IEnumerable<DNAEvaluator> evaluators)
		{
			_dnaEvaluators.AddRange(evaluators);
		}

		public bool Contains(DNAEvaluator evaluator)
		{
			return _dnaEvaluators.Contains(evaluator);
		}

		public void Clear()
		{
			_dnaEvaluators.Clear();
		}

		public int IndexOf(DNAEvaluator evaluator)
		{
			return _dnaEvaluators.IndexOf(evaluator);
		}

		public void Insert(int index, DNAEvaluator evaluator)
		{
			_dnaEvaluators.Insert(index, evaluator);
		}

		public void InsertRange(int index, IEnumerable<DNAEvaluator> evaluators)
		{
			_dnaEvaluators.InsertRange(index, evaluators);
		}

		public void Remove(DNAEvaluator evaluator)
		{
			_dnaEvaluators.Remove(evaluator);
		}

		public void RemoveAt(int index)
		{
			_dnaEvaluators.RemoveAt(index);
		}

		public void RemoveRange(int index, int count)
		{
			_dnaEvaluators.RemoveRange(index, count);
		}

		public DNAEvaluator[] ToArray()
		{
			return _dnaEvaluators.ToArray();
		}
		#endregion

		#region ATTRIBUTES
		[System.AttributeUsage(System.AttributeTargets.Field)]
		public class ConfigAttribute : System.Attribute
		{
			public enum LabelOptions
			{
				drawLabelAsFoldout,
				drawExpandedWithLabel,
				drawExpandedNoLabel
			}
			//if drawExpandedNoLabel the label for the list is shown as the heading for the dnaName field
			public LabelOptions labelOption = LabelOptions.drawLabelAsFoldout;
			//if set, when a new entry is added to the list using the UI it will be of this type
			public DNAEvaluationGraph defaultGraph = DNAEvaluationGraph.Default;

			/// <param name="labelOption">How to show the label for the list. If 'drawExpandedNoLabel' the label for the list is shown as the heading for the dnaName field</param>
			public ConfigAttribute(LabelOptions labelOption)
			{
				this.labelOption = labelOption;
			}
		}
		#endregion
}
}
