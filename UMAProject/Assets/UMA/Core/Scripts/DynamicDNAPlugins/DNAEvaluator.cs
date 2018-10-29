using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;

namespace UMA
{
	//A DNAEvaluator performs an math evaluation on an incoming dna value.
	//All over the place in our code we change the incoming dna value from its standard 0 -> 1 range to a -1 -> 1 range
	//but then in Morphset new code was needed in order to change the value into a value that could be used for 'PoseOne' or 'PoseZero'
	//The whole idea of DNAEvaluator (and its corresoponding DNAEvaluationGraph) is to rid us of the need to make new code every time 
	//we need to perform a different math calculation on a dna value (and to give this flexibility to users as well)
	[System.Serializable]
	public sealed class DNAEvaluator : ISerializationCallbackReceiver
	{
		//This is used with the Cumulative option in DNAEvaluatorList. Each line can be added/subtracted etc from the previous one
		public enum CalcOption
		{
			Add,
			Subtract,
			Multiply,
			Divide
		};
		[SerializeField]
		[Tooltip("Define how the evaluated value will be combined with the previous Evaluator in the list.")]
		private CalcOption _calcOption = CalcOption.Add;
		[SerializeField]
		[Tooltip("The DNA entry name to evaluate")]
		private string _dnaName;
		[SerializeField]
		[Tooltip("Evaluates the incoming dna value using the given graph. Hover the options for info")]
		private DNAEvaluationGraph _evaluator;
		[SerializeField]
		[Tooltip("The evaluated value will be multiplied by this value before it is returned.")]
		private float _multiplier = 1.0f;
		[SerializeField]
		[HideInInspector]
		private bool _initialized = false;

		//used at runtime to speed up finding dna values. The first time the evaluator evaluates it uses the dnaName, if the request successfully
		//returns an index, thereafter the index is used
		[System.NonSerialized]
		private int _lastIndex = -1;

		//if the evaluator fails to evaluate for any reason it returns this value
		//But should this be 0.5 or should it be 0? I'm using 0.5 because 'failSilently' in DynamicUMADNA returns 0.5 when a name isn't found
		public static readonly float defaultDNAValue = 0.5f;

		public CalcOption calcOption
		{
			get { return _calcOption; }
			set { _calcOption = value; }
		}

		public string dnaName
		{
			get { return _dnaName; }
			set { _dnaName = value; }
		}

		public DNAEvaluationGraph evaluator
		{
			get { return _evaluator; }
			set { _evaluator = new DNAEvaluationGraph(value); }
		}

		public float multiplier
		{
			get { return _multiplier; }
			set { _multiplier = value; }
		}

		public DNAEvaluator() { }

		public DNAEvaluator(string dnaName, DNAEvaluationGraph evaluator = null, float multiplier = 1f, CalcOption calcOption = CalcOption.Add)
		{
			_calcOption = calcOption;
			_dnaName = dnaName;
			_evaluator = evaluator == null ? DNAEvaluationGraph.Default : new DNAEvaluationGraph(evaluator);
			_multiplier = multiplier;
			_initialized = true;
		}

		public DNAEvaluator(DNAEvaluator other)
		{
			_calcOption = other.calcOption;
			_dnaName = other.dnaName;
			_evaluator = other.evaluator == null ? DNAEvaluationGraph.Default : new DNAEvaluationGraph(other.evaluator);
			_multiplier = other.multiplier;
			_initialized = true;
		}
		/// <summary>
		/// Applies the evaluator and the multiplier to the given dna value.
		/// </summary>
		/// <param name="dnaValue"></param>
		/// <returns>The evaluated value</returns>
		public float Evaluate(float dnaValue)
		{
			float val = dnaValue;
			return (_evaluator.Evaluate(val)) * _multiplier;
		}

		/// <summary>
		/// Finds the set dna name in the supplied dna list and applies the evaluator and multiplier to it. 
		/// Tip: if you are already looping through the dna to find the name/value just use Evaluate(float dnaValue) instead for efficiency
		/// </summary>
		/// <param name="dna">The dna to search</param>
		/// <returns>The evaluated value</returns>
		public float Evaluate(UMADnaBase dna)
		{
			//GRR this makes no difference to speed either-WTFF?!?
			if (_lastIndex != -1)
			{
				return Evaluate(dna.GetValue(_lastIndex));
			}
			else
			{
				_lastIndex = System.Array.IndexOf(dna.Names, _dnaName);
				if (_lastIndex > -1)
				{
					return Evaluate(dna.GetValue(_lastIndex));
				}
			}
			return defaultDNAValue;
		}

		#region ISerializationCallbackReceiver Members

		public void OnAfterDeserialize()
		{
		}
		//Fix to ensure that when instances of this class are added to a list in the inspector the default float values are set properly
		//GRR I hate this solution but I dont want users to have to set every new one of these in a list to be 1 rather than 0
		public void OnBeforeSerialize()
		{
			if (!_initialized)
			{
				_multiplier = 1f;
				_initialized = true;
			}
		}

		#endregion

		#region ATTRIBUTES

		//TODO Ditch these I always want this drawn the same way
		[System.AttributeUsage(System.AttributeTargets.Field)]
		public class ConfigAttribute : System.Attribute
		{
			public bool drawInline = true;
			public bool drawLabels = true;
			//the calc option is usually only needed with a list of Evaluators whose results will be added/subtracted etc from each other
			public bool drawCalcOption = false;
			public bool alwaysExpanded = false;

			public ConfigAttribute(bool drawInline)
			{
				this.drawInline = drawInline;
			}
			public ConfigAttribute(bool drawInline, bool drawLabels)
			{
				this.drawInline = drawInline;
				this.drawLabels = drawLabels;
			}
			public ConfigAttribute(bool drawInline, bool drawLabels, bool alwaysExpanded)
			{
				this.drawInline = drawInline;
				this.drawLabels = drawLabels;
				this.alwaysExpanded = alwaysExpanded;
			}
			public ConfigAttribute(bool drawInline, bool drawLabels, bool alwaysExpanded, bool drawCalcOption)
			{
				this.drawInline = drawInline;
				this.drawLabels = drawLabels;
				this.alwaysExpanded = alwaysExpanded;
				this.drawCalcOption = drawCalcOption;
			}

		}

		#endregion

		}
	}
