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
			Minumum,
			Maximum,
			CumulativeNormalized
		};

		[SerializeField]
		private List<DNAEvaluator> _dnaEvaluators = new List<DNAEvaluator>();

		[SerializeField]
		[Tooltip("How the evaluated results of each entry are combined and returned.(CumulativeNormalized subtracts 0.5f from the incoming value to make the default UMA dna value of 0.5f zero)")]
		private AggregationMethodOpts _aggregationMethod = AggregationMethodOpts.Average;

		#region CONSTRUCTOR

		public DNAEvaluatorList()
		{
			//make sure the list has an empty element in
			_dnaEvaluators.Add(new DNAEvaluator());
		}

		public DNAEvaluatorList(DNAEvaluatorList other)
		{
			_aggregationMethod = other._aggregationMethod;
			for(int i = 0; i < other._dnaEvaluators.Count; i++)
			{
				_dnaEvaluators.Add(new DNAEvaluator(other._dnaEvaluators[i]));
			}
		}

		#endregion

		#region METHODS

		/// <summary>
		/// For each evaluator in the list, finds the set dna name in the supplied dna list and applies the evaluator and multiplier to it. The resulting values are then aggregated together using the method defined in the 'AggregationMethod' field
		/// </summary>
		/// <param name="dna">The dna to search</param>
		/// <returns>The evaluated value</returns>
		public float Evaluate(DynamicUMADnaBase dna)
		{
			if(_dnaEvaluators.Count > 0)
			{
				List<float> results = new List<float>();
				//loop the dna to find all the dna name value pairs we need because getting dna by name is slow
				Dictionary<int, float> incomingValues = new Dictionary<int, float>();
				//get the incoming values
				for(int i = 0; i < _dnaEvaluators.Count; i++)
				{
					if (!string.IsNullOrEmpty(_dnaEvaluators[i].dnaName))
					{
						var _dnaIndex = System.Array.IndexOf(dna.Names, _dnaEvaluators[i].dnaName);
						if (_dnaIndex > -1)
						{
							incomingValues.Add(i, dna.GetValue(_dnaIndex));
						}
					}
				}
				//evaluate the incoming values
				for (int i = 0; i < _dnaEvaluators.Count; i++)
				{
					if (incomingValues.ContainsKey(i))
					{
						results.Add(_dnaEvaluators[i].Evaluate(incomingValues[i]));
					}
				}
				return GetAggregateValue(results);
			}
			else
			{
				return DNAEvaluator.defaultDNAValue;//not sure if this should be 0 or 0.5f yet
			}
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
			if(_aggregationMethod == AggregationMethodOpts.CumulativeNormalized)
			{
				for (int i = 0; i < vals.Count; i++)
				{
					result += (vals[i] - 0.5f);
				}
			}
			if(_aggregationMethod == AggregationMethodOpts.Minumum)
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
	}
}
