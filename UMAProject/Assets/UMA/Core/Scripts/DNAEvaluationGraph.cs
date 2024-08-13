using System.Collections.Generic;
using UnityEngine;

namespace UMA
{

    //A dna evaluaton graph is used so that we dont need to hard code things like the math calcs for PoseOne and PoseZero in a MorphDNASet
    //we can use a linear animation graph and that can do that evaluation for us based on the incoming dna value. 
    //Basically there is no need to hard code other behaviours now just because you need the dna interpreted differently.

    //The incoming dna value is the horizontal axis on the graph and what it returns is the value on the vertical axis at that point.
    //this class defines the graph class and provides lots of handy defaults in the same way Color does with Color.red, Color.blue etc
    //Theres loads of help in the instance of DNAEvaluationGraphPresetLibrary in the project that hopefully makes it really clear.

    [System.Serializable]
	public sealed class DNAEvaluationGraph : System.IEquatable<DNAEvaluationGraph>
	{

		[SerializeField]
		private string _name;
		[SerializeField]
		private AnimationCurve _graph;

		public DNAEvaluationGraph() { }


		public DNAEvaluationGraph(string name, AnimationCurve graph)
		{
			this._name = name;
			this._graph = new AnimationCurve(graph.keys);
		}

		public DNAEvaluationGraph(DNAEvaluationGraph other)
		{
			this._name = other.name;
			this._graph = new AnimationCurve(other._graph.keys);
		}


		public string name
		{
			get { return _name; }
		}

		public Keyframe[] GraphKeys
		{
			get { return _graph.keys; }
		}

		/// <summary>
		///   <para>Evaluate the dnaValue using the graph.</para>
		/// </summary>
		/// <param name="dnaValue">The dnaValue within the graph you want to evaluate (the horizontal axis in the graph).</param>
		/// <returns>
		///   <para>The value of the graph, at the point specified.</para>
		/// </returns>
		public float Evaluate(float dnaValue)
		{
			return _graph.Evaluate(dnaValue);
		}

		/// <summary>
		/// Returns true if this DNAEvaluationGraphs graph is the same as another ones graph
		/// </summary>
		/// <param name="other">The other DNAEvaluationGraph to check</param>
		/// <returns></returns>
		public bool GraphMatches(DNAEvaluationGraph other)
		{
			return GraphMatches(other._graph);
		}
		/// <summary>
		/// Returns true if this DNAEvaluationGraphs graph is the same as the given AnimationCurve 
		/// </summary>
		/// <param name="animCurve">The AnimationCurve to compare</param>
		/// <returns></returns>
		public bool GraphMatches(AnimationCurve animCurve)
		{
			//I *think* == on an animationCurve only tells us
			//if the two instance are the same as one another 
			//rather than whether two curves have the same keys so...
			if (this._graph == null && animCurve == null)
            {
                return true;
            }

            if (this._graph == null && animCurve != null)
            {
                return false;
            }

            if (this._graph != null && animCurve == null)
            {
                return false;
            }

            if (this._graph.keys.Length == animCurve.keys.Length)
			{
				if (this._graph.keys.Length == 0 && animCurve.keys.Length == 0)
				{
					return true;
				}
				int matchingKeys = 0;
				for (int i = 0; i < this._graph.keys.Length; i++)
				{
					if (this._graph.keys[i].time == animCurve.keys[i].time &&
						this._graph.keys[i].value == animCurve.keys[i].value &&
						this._graph.keys[i].inTangent == animCurve.keys[i].inTangent &&
						this._graph.keys[i].outTangent == animCurve.keys[i].outTangent /*&&
						this._graph.keys[i].tangentMode == animCurve.keys[i].tangentMode*/)//tangentMode is obsolete outside the editor as of Unity2018
					{
						matchingKeys++;
					}
				}
				if (matchingKeys == this._graph.keys.Length)
                {
                    return true;
                }
            }
			return false;
		}

		#region IEquatableInterface

		public static implicit operator bool(DNAEvaluationGraph obj)
		{
			return ((System.Object)obj) != null;
		}

		public bool Equals(DNAEvaluationGraph other)
		{
			return (this == other);
		}
		public override bool Equals(object other)
		{
			return Equals(other as DNAEvaluationGraph);
		}

		public static bool operator ==(DNAEvaluationGraph cd1, DNAEvaluationGraph cd2)
		{
			return (Compare(cd1, cd2)) > 0;
		}

		public static bool operator !=(DNAEvaluationGraph cd1, DNAEvaluationGraph cd2)
		{
			return (Compare(cd1, cd2)) == 0;
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		#endregion

		#region IComparerInterface
		/// <summary>
		/// Compares the given DNAEvaluationGraphs to see if they are equal to each other
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <returns>Returns 0 if they are NOT the same returns 1 if they are the same (and if they are both null)</returns>
		private static int Compare(object x, object y)
		{
			if (((System.Object)x) == null && ((System.Object)y) == null)
            {
                return 1;
            }

            if (((System.Object)x) == null && ((System.Object)y) != null)
            {
                return 0;
            }

            if (((System.Object)x) != null && ((System.Object)y) == null)
            {
                return 0;
            }

            var xo = (x as DNAEvaluationGraph);

			var yo = (y as DNAEvaluationGraph);

			if (xo.name == yo.name)
			{
				if (xo._graph != null && yo._graph != null)
				{
					if (xo._graph.keys.Length == yo._graph.keys.Length)
					{
						if (xo._graph.keys.Length == 0 && yo._graph.keys.Length == 0)
						{
							return 1;
						}
						int matchingKeys = 0;
						for (int i = 0; i < xo._graph.keys.Length; i++)
						{
							if (xo._graph.keys[i].time == yo._graph.keys[i].time &&
								xo._graph.keys[i].value == yo._graph.keys[i].value &&
								xo._graph.keys[i].inTangent == yo._graph.keys[i].inTangent &&
								xo._graph.keys[i].outTangent == yo._graph.keys[i].outTangent /*&&
								xo._graph.keys[i].tangentMode == yo._graph.keys[i].tangentMode*/)//tangentMode is obsolete outside the editor as of Unity2018
							{
								matchingKeys++;
							}
						}
						if (matchingKeys == xo._graph.keys.Length)
                        {
                            return 1;
                        }

                        return 0;
					}
					return 0;
				}
				else if (xo._graph == null && yo._graph == null)
				{
					return 1;
				}
				return 0;
			}
			return 0;
		}

		#endregion

		#region DEFAULTS

		//The following are like Color.blue or Color.red etc
		//Should the 'Default' graph be the calculation that we *always* do (i.e. ((value - 0.5f) *2) which would be a graph that went was 0 to 1 horizontally and was -1 to +1 vertically)
		//I think so, so I have made another graph that just returns the incoming dna value and called it 'Raw'

		//I have also made a whole Presets thing aswell thats the equivalent to preset colors in the color chooser or preset animation curves with animation curve
		//Check out DNAEvaluationGraphPresetLibrary for this, or an instance of it in the inspector- its like an animationCurve preset library on crack.

		/// <summary>
		/// Performs the default math calculation on the incoming dna value, subtracting 0.5f making its range -0.5f -> 0.5f
		/// </summary>
		public static DNAEvaluationGraph Default
		{
			get
			{
				return new DNAEvaluationGraph(
					"Default",
					new AnimationCurve(new Keyframe(0f, -0.5f, 0f, 1f), new Keyframe(0.5f, 0f, 1f, 1f), new Keyframe(1f, 0.5f, 1f, 2f))
					);
			}
		}
		/// <summary>
		///  Performs the default math calculation on the incoming dna value and inverts it, extending its range from 0f -> 1f to 1f -> -1f
		/// </summary>
		public static DNAEvaluationGraph DefaultInverted
		{
			get
			{
				return new DNAEvaluationGraph(
					"DefaultInverted",
					new AnimationCurve(new Keyframe(0f, 0.5f, 0f, -1f), new Keyframe(0.5f, 0f, -1f, -1f), new Keyframe(1f, -0.5f, -1f, 2f))
					);
			}
		}
		/// <summary>
		/// Performs the default math calculation on the incoming dna value, and extends its range from 0f -> 1f to -1f -> 1f
		/// </summary>
		public static DNAEvaluationGraph DefaultOne
		{
			get
			{
				return new DNAEvaluationGraph(
					"DefaultOne",
					new AnimationCurve(new Keyframe(0f, -1f, 0f, 2f), new Keyframe(0.5f, 0f, 2f, 2f), new Keyframe(1f, 1f, 2f, 1f))
					);
			}
		}
		/// <summary>
		///  Performs the default math calculation on the incoming dna value and inverts it, extending its range from 0f -> 1f to 1f -> -1f
		/// </summary>
		public static DNAEvaluationGraph DefaultOneInverted
		{
			get
			{
				return new DNAEvaluationGraph(
					"DefaultOneInverted",
					new AnimationCurve(new Keyframe(0f, 1f, 0f, -2f), new Keyframe(0.5f, 0f, -2f, -2f), new Keyframe(1f, -1f, -2f, 1f))
					);
			}
		}
		/// <summary>
		/// The returned value remains at zero until it is greater than 0.5 when it increases as the incoming value approaches 1f
		/// </summary>
		public static DNAEvaluationGraph ZeroZeroOne
		{
			get
			{
				return new DNAEvaluationGraph(
					"ZeroZeroOne",
					new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 0f, 0f, 2f), new Keyframe(1f, 1f, 2f, 0f))
					);
				}
		}
		/// <summary>
		/// Returns 1f when the dna value is zero and decreases as the dna approahes 0.5f, thereafter returns zero
		/// </summary>
		public static DNAEvaluationGraph OneZeroZero
		{
			get
			{
				return new DNAEvaluationGraph(
					"OneZeroZero",
					new AnimationCurve(new Keyframe(0f, 1f, 0f, -2f), new Keyframe(0.5f, 0f, -2f, 0f), new Keyframe(1f, 0f, 0f, 0f))
					);
			}
		}
		/// <summary>
		/// Returns 0f when the dna value is zero, 1f when dna value is 0.5f, and zero when dna value is 1f
		/// </summary>
		public static DNAEvaluationGraph ZeroOneZero
		{
			get
			{
				return new DNAEvaluationGraph(
					"ZeroOneZero",
					new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(0.5f, 1f, 2f, -2f), new Keyframe(1f, 0f, -2f, 1f))
					);
			}
		}
		/// <summary>
		/// Returns 1f when the dna value is zero, 0f when dna value is 0.5f, and 1f when dna value is 1f
		/// </summary>
		public static DNAEvaluationGraph OneZeroOne
		{
			get
			{
				return new DNAEvaluationGraph(
					"OneZeroOne",
					new AnimationCurve(new Keyframe(0f, 1f, 0f, -2f), new Keyframe(0.5f, 0f, -2f, 2f), new Keyframe(1f, 1f, 2f, 1f))
					);
			}
		}
		/// <summary>
		/// Returns 0f when the dna value is zero, fading to 1f when dna value is 0.5f, and 1f thereafter
		public static DNAEvaluationGraph ZeroOneOne
		{
			get
			{
				return new DNAEvaluationGraph(
					"ZeroOneOne",
					new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(0.5f, 1f, 2f, 0f), new Keyframe(1f, 1f, -0f, 1f))
					);
			}
		}
		/// <summary>
		/// Returns 1f when the dna value is zero, 1f when dna value is 0.5f, fading to zero thereafter
		public static DNAEvaluationGraph OneOneZero
		{
			get
			{
				return new DNAEvaluationGraph(
					"OneOneZero",
					new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(0.5f, 1f, -0f, -2f), new Keyframe(1f, 0f, -2f, 1f))
					);
			}
		}
		/// <summary>
		/// Simply returns the raw dna value which will be somewhere between 0 and 1
		/// </summary>
		public static DNAEvaluationGraph Raw
		{
			get
			{
				return new DNAEvaluationGraph(
					"Raw",
					new AnimationCurve(new Keyframe(0f, 0f, 0f, 1f), new Keyframe(0.5f, 0.5f, 1f, 1f), new Keyframe(1f, 1f, 1f, 0f))
					);
			}
		}
		/// <summary>
		/// Simply returns the raw dna value but inverted, a incoming value of 1 will be 0 and vice versa
		/// </summary>
		public static DNAEvaluationGraph RawInverted
		{
			get
			{
				return new DNAEvaluationGraph(
					"RawInverted",
					new AnimationCurve(new Keyframe(0f, 1f, 0f, -1f), new Keyframe(0.5f, 0.5f, -1f, -1f), new Keyframe(1f, 0f, -1f, 0f))
					);
			}
		}
		public static string DefaultToolTip
		{
			get { return "The Default DNA Calculation. Subtracts 0.5f from the incoming value, making its range -0.5f -> 0.5f and making 0.5f = 0f"; }
		}
		public static string DefaultInvertedToolTip
		{
			get { return "The Default DNA Calculation but inverted. Subtracts 0.5f from the incoming value and multiplies by -1f, making its range 0.5f -> -0.5f and making 0.5f = 0f"; }
		}
		public static string DefaultOneToolTip
		{
			get { return "Subtracts 0.5f from the incoming value and doubles the result, extending its range from 0f -> 1f to -1f -> 1f"; }
		}
		public static string DefaultOneInvertedToolTip
		{
			get { return "Subtracts 0.5f from the incoming value, doubles the result, and inverts it, extending its range from 0f -> 1f to 1f -> -1f"; }
		}
		public static string ZeroZeroOneToolTip
		{
			get { return "The returned value remains at zero until it is greater than 0.5 when it increases as the incoming value approaches 1f"; }
		}
		public static string OneZeroZeroToolTip
		{
			get { return "Returns 1f when the dna value is zero and decreases as the dna approahes 0.5f, thereafter returns zero"; }
		}
		public static string ZeroOneZeroToolTip
		{
			get { return "Returns 0f when the dna value is zero, 1f when dna value is 0.5f, and zero when dna value is 1f"; }
		}
		public static string OneZeroOneToolTip
		{
			get { return "Returns 1f when the dna value is zero, 0f when dna value is 0.5f, and 1f when dna value is 1f"; }
		}
		public static string ZeroOneOneToolTip
		{
			get { return "Returns 0f when the dna value is zero, fading to 1f when dna value is 0.5f, and 1f thereafter"; }
		}
		public static string OneOneZeroToolTip
		{
			get { return "Returns 1f when the dna value is zero, 1f when dna value is 0.5f, fading to zero thereafter"; }
		}
		public static string RawToolTip
		{
			get { return "Simply returns the raw dna value which will be somewhere between 0 and 1"; }
		}
		public static string RawInvertedToolTip
		{
			get { return "Simply returns the raw dna value but inverted, a incoming value of 1 will be 0 and vice versa"; }
		}

		/// <summary>
		/// Returns a dictionary of all the default DNAEvaluationGraphs and their corresponding tooltips
		/// </summary>
		/// <returns></returns>
		public static Dictionary<DNAEvaluationGraph, string> Defaults
		{
			get
			{
				var dict = new Dictionary<DNAEvaluationGraph, string>
				{
					{ Default, DefaultToolTip },
					{ DefaultInverted, DefaultInvertedToolTip },
					{ DefaultOne, DefaultOneToolTip },
					{ DefaultOneInverted, DefaultOneInvertedToolTip },
					{ ZeroZeroOne, ZeroZeroOneToolTip },
					{ OneZeroZero, OneZeroZeroToolTip },
					{ ZeroOneZero, ZeroOneZeroToolTip },
					{ OneZeroOne, OneZeroOneToolTip },
					{ OneOneZero, OneOneZeroToolTip },
					{ ZeroOneOne, ZeroOneOneToolTip },
					{ Raw, RawToolTip },
					{ RawInverted, RawInvertedToolTip },
				};

				return dict;
			}
		}
		#endregion

		#region Special Types

		public class EditorHelper
		{
			private DNAEvaluationGraph _evaluationGraph;

			public EditorHelper()
			{
				_evaluationGraph = new DNAEvaluationGraph();
			}

			public EditorHelper(AnimationCurve graph, string name, string description)
			{
				_evaluationGraph = new DNAEvaluationGraph(name, graph);
			}

			public EditorHelper(DNAEvaluationGraph dnaEasingCurve)
			{
				_evaluationGraph = new DNAEvaluationGraph(dnaEasingCurve);
			}

			public DNAEvaluationGraph Target
			{
				get { return _evaluationGraph; }
			}

			public string _name
			{
				get { return _evaluationGraph._name; }
				set { _evaluationGraph._name = value; }
			}

			public AnimationCurve _graph
			{
				get { return _evaluationGraph._graph; }
				set {
					if (value == null)
                    {
                        _evaluationGraph._graph = value;
                    }
                    else
					{
						_evaluationGraph._graph = new AnimationCurve(value.keys);
					}
				}
			}

		}

		#endregion
	}
}
