using UnityEngine;

namespace UMA
{
    //A DNAEvaluator performs an math evaluation on an incoming dna value.
    //All over the place in our code we change the incoming dna value from its standard 0 -> 1 range to a -0.5 -> 0.5 range
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
		//this could be used to search dnaNames by hash- I did some tests but could not see any speed improvement
		[SerializeField]
		private int _dnaNameHash = -1;
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
		//this has a minor impact on speed for lookups
		[System.NonSerialized]
		private int _lastIndex = -1;
		

		//if the evaluator fails to evaluate for any reason it returns this value
		//TODO Double check this doesn't need to return the raw value (i.e. 0.5f)
		public static readonly float defaultDNAValue = 0f;

		public CalcOption calcOption
		{
			get { return _calcOption; }
			set { _calcOption = value; }
		}

		public string dnaName
		{
			get { return _dnaName; }
			set
			{
				_dnaName = value;
				_dnaNameHash = UMAUtils.StringToHash(_dnaName);
			}
		}

		public int dnaNameHash
		{
			get { return _dnaNameHash; }
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
			_dnaNameHash = UMAUtils.StringToHash(_dnaName);
			_evaluator = evaluator == null ? DNAEvaluationGraph.Default : new DNAEvaluationGraph(evaluator);
			_multiplier = multiplier;
			_initialized = true;
		}

		public DNAEvaluator(DNAEvaluator other)
		{
			_calcOption = other.calcOption;
			_dnaName = other.dnaName;
			_dnaNameHash = UMAUtils.StringToHash(_dnaName);
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
			//using    lastIndex building dna takes apprx  00:00:00.0027695
			//Using    lastIndex modifying dna takes apprx 00:00:00.0004993
			//notusing lastIndex building dna takes apprx  00:00:00.0035695
			//notusing lastIndex modifying dna takes apprx 00:00:00.0008447
			//using lastIndex is about 1/3 faster at the cost of being less robust because the dnaNames could in theory be changed at runtime and lastIndex would then fail
			//could make the difference between being able to use things like color dna for wrinkle maps etc?
			if (_lastIndex != -1)
			{
				return Evaluate(dna.GetValue(_lastIndex));
			}
			else
			{
				if (!string.IsNullOrEmpty(_dnaName) && dna.Names != null)
				{
					_lastIndex = System.Array.IndexOf(dna.Names, _dnaName);
					if (_lastIndex > -1)
					{
						return Evaluate(dna.GetValue(_lastIndex));
					}
				}
			}
			return defaultDNAValue;
		}

		#region ISerializationCallbackReceiver Members

		public void OnAfterDeserialize()
		{

		}
		//Fix to ensure that when instances of this class are added to a list in the inspector the default values are set properly
		//GRR I hate this solution but I dont want users to have to set every new one of these to have a multiplier of 1 and if the dnaName is empty the hash needs to be -1
		public void OnBeforeSerialize()
		{
			if (!_initialized)
			{
				if (!string.IsNullOrEmpty(_dnaName))
                {
                    _dnaNameHash = UMAUtils.StringToHash(_dnaName);
                }
                else
                {
                    _dnaNameHash = -1;
                }

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
			public bool drawLabels = true;
			//the calc option is usually only needed with a list of Evaluators whose results will be added/subtracted etc from each other
			public bool drawCalcOption = false;
			public bool alwaysExpanded = false;

			public ConfigAttribute(bool drawLabels)
			{
				this.drawLabels = drawLabels;
			}
			public ConfigAttribute(bool drawLabels, bool alwaysExpanded)
			{
				this.drawLabels = drawLabels;
				this.alwaysExpanded = alwaysExpanded;
			}
			public ConfigAttribute( bool drawLabels, bool alwaysExpanded, bool drawCalcOption)
			{
				this.drawLabels = drawLabels;
				this.alwaysExpanded = alwaysExpanded;
				this.drawCalcOption = drawCalcOption;
			}

		}

		#endregion

		}
	}
