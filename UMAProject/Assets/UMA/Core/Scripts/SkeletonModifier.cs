using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace UMA.CharacterSystem
{
    //To enable us to change how this works in the future I just went the whole hog and changed all the public fields to private with public property get/setters
    [Serializable]
	public class SkeletonModifier
	{
		public enum SkeletonPropType { Position, Rotation, Scale }

		[FormerlySerializedAs("hashName")]
		[SerializeField]
		private string _hashName;

		[FormerlySerializedAs("hash")]
		[SerializeField]
		private int _hash;

		[FormerlySerializedAs("property")]
		[SerializeField]
		private SkeletonPropType _property = SkeletonPropType.Position;

		[FormerlySerializedAs("valuesX")]
		[SerializeField]
		private spVal _valuesX;

		[FormerlySerializedAs("valuesY")]
		[SerializeField]
		private spVal _valuesY;

		[FormerlySerializedAs("valuesZ")]
		[SerializeField]
		private spVal _valuesZ;

		[FormerlySerializedAs("umaDNA")]
		[SerializeField]
		private UMADnaBase _umaDNA;

		public string hashName
		{
			get { return _hashName; }
			set { _hashName = value; }
		}

		public int hash
		{
			get { return _hash; }
			set { hash = value; }
		}

		public SkeletonPropType property
		{
			get { return _property; }
			set { _property = value; }
		}

		public spVal valuesX
		{
			get { return _valuesX; }
			set { _valuesX = new spVal(value); }
		}

		public spVal valuesY
		{
			get { return _valuesY; }
			set { _valuesY = new spVal(value); }
		}

		public spVal valuesZ
		{
			get { return _valuesZ; }
			set { _valuesZ = new spVal(value); }
		}

		//this only needs setting at runtime- confirm
		public UMADnaBase umaDNA
		{
			get { return _umaDNA; }
			set { _umaDNA = value; }
		}
		/// <summary>
		/// A dictionary of skeletonModifier default values by SkeletonPropType where the x value is the default value for the property the y value is the default clamp min and the z value is the default clamp max
		/// </summary>
		public static Dictionary<SkeletonPropType, Vector3> skelAddDefaults = new Dictionary<SkeletonPropType, Vector3>
			{
				{SkeletonPropType.Position, new Vector3(0f,-0.1f, 0.1f) },
				{SkeletonPropType.Rotation, new Vector3(0f,-360f, 360f) },
				{SkeletonPropType.Scale,  new Vector3(1f,0f, 5f) }
			};

		[Obsolete("Please use CalculateValueX((UMADnaBase umaDNA) instead")]
		public Vector3 ValueX
		{
			get { return _valuesX.CalculateValue(_umaDNA); }
		}
		[Obsolete("Please use CalculateValueY((UMADnaBase umaDNA) instead")]
		public Vector3 ValueY
		{
			get { return _valuesY.CalculateValue(_umaDNA); }
		}
		[Obsolete("Please use CalculateValueZ((UMADnaBase umaDNA) instead")]
		public Vector3 ValueZ
		{
			get { return _valuesZ.CalculateValue(_umaDNA); }
		}

		public SkeletonModifier() { }

		public SkeletonModifier(string _hashName, int _hash, SkeletonPropType _propType)
		{
			this._hashName = _hashName;
			this._hash = _hash;
			this._property = _propType;
			this._valuesX = new spVal(skelAddDefaults[_propType]);
			this._valuesY = new spVal(skelAddDefaults[_propType]);
			this._valuesZ = new spVal(skelAddDefaults[_propType]);
		}

		public SkeletonModifier(SkeletonModifier importedModifier, bool doUpgrade = false)
		{
			this._hashName = importedModifier._hashName;
			this._hash = importedModifier._hash;
			this._property = importedModifier._property;
			this._valuesX = new spVal(importedModifier._valuesX);
			this._valuesY = new spVal(importedModifier._valuesY);
			this._valuesZ = new spVal(importedModifier._valuesZ);
			this._umaDNA = importedModifier._umaDNA;//dont think this should have ever been serialized
			if (doUpgrade)
            {
                UpgradeToDNAEvaluators();
            }
        }
		//When happy get rid of this and do it via ISerializationCallbacks instead
		public void UpgradeToDNAEvaluators()
		{
			_valuesX.val.ConvertToDNAEvaluators();
			_valuesY.val.ConvertToDNAEvaluators();
			_valuesZ.val.ConvertToDNAEvaluators();
		}

		public Vector3 CalculateValueX(UMADnaBase umaDNA)
		{
			var resVal = _valuesX.CalculateValue(_umaDNA);
			return resVal;
		}

		public Vector3 CalculateValueY(UMADnaBase umaDNA)
		{
			var resVal = _valuesY.CalculateValue(_umaDNA);
			return resVal;
		}

		public Vector3 CalculateValueZ(UMADnaBase umaDNA)
		{
			var resVal = _valuesZ.CalculateValue(_umaDNA);
			return resVal;
		}

		//Skeleton Modifier Special Types
		[Serializable]
		public class spVal
		{
			[FormerlySerializedAs("val")]
			[SerializeField]
			private spValValue _val;
			[FormerlySerializedAs("min")]
			[SerializeField]
			private float _min = 1;
			[FormerlySerializedAs("max")]
			[SerializeField]
			private float _max = 1;

			public spValValue val
			{
				get { return _val; }
				set { _val = value; }
			}

			public float min
			{
				get { return _min; }
				set { _min = value; }
			}

			public float max
			{
				get { return _max; }
				set { _max = value; }
			}

			public spVal() { }

			public spVal(Vector3 startingVals)
			{
				_val = new spValValue();
				_val.value = startingVals.x;
				_min = startingVals.y;
				_max = startingVals.z;
			}

#pragma warning disable 618 //disable obsolete warning
			public spVal(spVal importedSpVal)
			{
				_val = new spValValue();
				_val.value = importedSpVal._val.value;
				_val.modifiers = new List<spValValue.spValModifier>(importedSpVal._val.modifiers);
				_val.modifyingDNA = new DNAEvaluatorList(importedSpVal._val.modifyingDNA);
				_min = importedSpVal._min;
				_max = importedSpVal._max;
			}
#pragma warning restore 618 //disable obsolete warning

			public Vector3 CalculateValue(UMADnaBase umaDNA)
			{
				var thisVal = new Vector3();
				//val
				thisVal.x = _val.CalculateValue(umaDNA);
				//valmin
				thisVal.y = _min;
				//max
				thisVal.z = _max;
				return thisVal;
			}

			//spVal Special Types
			[Serializable]
			public class spValValue
			{
				[FormerlySerializedAs("value")]
				[SerializeField]
				private float _value = 0f;

				//Mark as Obsolete
				[FormerlySerializedAs("modifiers")]
				[SerializeField]
				private List<spValModifier> _modifiers = new List<spValModifier>();

				[SerializeField]
				[DNAEvaluatorList.Config(DNAEvaluatorList.ConfigAttribute.LabelOptions.drawExpandedWithLabel)]
				[Tooltip("A list of dna that will be used to modify the bone on this axis. Usually you use 'Cumulative' so that the initial value for the axis is modified by each line here in turn.")]
				private DNAEvaluatorList _modifyingDNA = new DNAEvaluatorList(DNAEvaluatorList.AggregationMethodOpts.Cumulative);

				public float value
				{
					get { return _value; }
					set { _value = value; }
				}

				[System.Obsolete("Will be removed in future version. Please use 'modifyingDNA' instead")]
				public List<spValModifier> modifiers
				{
					get { return _modifiers; }
					set { _modifiers = value; }
				}

				public DNAEvaluatorList modifyingDNA
				{
					get { return _modifyingDNA; }
					set { _modifyingDNA = new DNAEvaluatorList(value); }
				}

				public float CalculateValue(UMADnaBase umaDNA)
				{
					float thisVal = _value;
					if (_modifiers.Count > 0 && _modifyingDNA.Count == 0)
					{
						thisVal = CalculateLegacyModifiers(thisVal, _modifiers, umaDNA);
					}
					if(_modifyingDNA.Count > 0)
					{
						var modDNAResult = _modifyingDNA.ApplyDNAToValue(umaDNA, _value);
						thisVal = modDNAResult;
					}
					return thisVal;
				}

				private float CalculateLegacyModifiers(float startingVal, List<spValModifier> _modifiers, UMADnaBase umaDNA)
				{
					float modifierVal = 0;
					float tempModifierVal = 0;
					string dnaCombineMethod = "";
					bool inModifierPair = false;
					for (int i = 0; i < _modifiers.Count; i++)
					{
						if (_modifiers[i].DNATypeName != "None" && (_modifiers[i].modifier == spValModifier.spValModifierType.AddDNA ||
							_modifiers[i].modifier == spValModifier.spValModifierType.DivideDNA ||
							_modifiers[i].modifier == spValModifier.spValModifierType.MultiplyDNA ||
							_modifiers[i].modifier == spValModifier.spValModifierType.SubtractDNA))
						{
							tempModifierVal = GetUmaDNAValue(_modifiers[i].DNATypeName, umaDNA);
							tempModifierVal -= 0.5f;
							inModifierPair = true;
							if (_modifiers[i].modifier == spValModifier.spValModifierType.AddDNA)
							{
								dnaCombineMethod = "Add";
							}
							else if (_modifiers[i].modifier == spValModifier.spValModifierType.DivideDNA)
							{
								dnaCombineMethod = "Divide";
							}
							else if (_modifiers[i].modifier == spValModifier.spValModifierType.MultiplyDNA)
							{
								dnaCombineMethod = "Multiply";
							}
							else if (_modifiers[i].modifier == spValModifier.spValModifierType.SubtractDNA)
							{
								dnaCombineMethod = "Subtract";
							}
						}
						else
						{
							if (_modifiers[i].modifier == spValModifier.spValModifierType.Add)
							{
								modifierVal += (tempModifierVal + _modifiers[i].modifierValue);
								tempModifierVal = 0;
								inModifierPair = false;
							}
							else if (_modifiers[i].modifier == spValModifier.spValModifierType.Divide)
							{
								modifierVal += (tempModifierVal / _modifiers[i].modifierValue);
								tempModifierVal = 0;
								inModifierPair = false;
							}
							else if (_modifiers[i].modifier == spValModifier.spValModifierType.Multiply)
							{
								modifierVal += (tempModifierVal * _modifiers[i].modifierValue);
								tempModifierVal = 0;
								inModifierPair = false;
							}
							else if (_modifiers[i].modifier == spValModifier.spValModifierType.Subtract)
							{
								modifierVal += (tempModifierVal - _modifiers[i].modifierValue);
								tempModifierVal = 0;
								inModifierPair = false;
							}
						}
						if (modifierVal != 0 && inModifierPair == false)
						{
							if (dnaCombineMethod == "Add")
							{
								startingVal += modifierVal;
							}
							if (dnaCombineMethod == "Subtract")
							{
								startingVal -= modifierVal;
							}
							if (dnaCombineMethod == "Multiply")
							{
								startingVal *= modifierVal;
							}
							if (dnaCombineMethod == "Divide")
							{
								startingVal /= modifierVal;
							}
							modifierVal = 0;
							dnaCombineMethod = "";
						}
					}
					//in the case of left/Right(Up)LegAdjust the umadna is subtracted from the result without being multiplied by anything
					//this accounts for the scenario where umaDna is left trailing with no correcponding add/subtract/multiply/divide multiplier
					if (tempModifierVal != 0 && inModifierPair != false)
					{
						if (dnaCombineMethod == "Add")
						{
							startingVal += tempModifierVal;
						}
						if (dnaCombineMethod == "Subtract")
						{
							startingVal -= tempModifierVal;
						}
						if (dnaCombineMethod == "Multiply")
						{
							startingVal *= tempModifierVal;
						}
						if (dnaCombineMethod == "Divide")
						{
							startingVal /= tempModifierVal;
						}
						dnaCombineMethod = "";
						modifierVal = 0;
						tempModifierVal = 0;
						inModifierPair = false;
					}
					return startingVal;
				}

				//Obsolete
				public float GetUmaDNAValue(string DNATypeName, UMADnaBase umaDnaIn)
				{
					if (umaDnaIn == null)
                    {
                        return 0.5f;
                    }

                    DynamicUMADnaBase umaDna = (DynamicUMADnaBase)umaDnaIn;
					float val = 0.5f;
					if (DNATypeName == "None" || umaDna == null)
					{
						return val;
					}
					val = umaDna.GetValue(DNATypeName, true);//implimented a 'failSilently' option here because recipes may have dna in that the dna asset no longer has
					return val;
				}

#pragma warning disable 618 //disable obsolete warning

				//make this staic?
				public void ConvertToDNAEvaluators()
				{
					spValModifier accessoryMod = null;
					DNAEvaluator.CalcOption calcOption = DNAEvaluator.CalcOption.Add;
					float multiplier = 1f;
					if (_modifiers.Count > 0 && _modifyingDNA.Count == 0)
					{
						_modifyingDNA.Clear();
						for (int i = 0; i < modifiers.Count; i++)
						{
							accessoryMod = null;
							multiplier = 1f;
							if (!string.IsNullOrEmpty(modifiers[i].DNATypeName) && modifiers[i].modifier.ToString().IndexOf("DNA") > -1)
							{
								if ((i + 1) < modifiers.Count && modifiers[i + 1].modifier.ToString().IndexOf("DNA") < 0)
                                {
                                    accessoryMod = modifiers[i + 1];
                                }

                                if (modifiers[i].modifier == spValModifier.spValModifierType.AddDNA)
                                {
                                    calcOption = DNAEvaluator.CalcOption.Add;
                                }
                                else if (modifiers[i].modifier == spValModifier.spValModifierType.DivideDNA)
                                {
                                    calcOption = DNAEvaluator.CalcOption.Divide;
                                }
                                else if (modifiers[i].modifier == spValModifier.spValModifierType.MultiplyDNA)
                                {
                                    calcOption = DNAEvaluator.CalcOption.Multiply;
                                }
                                else if (modifiers[i].modifier == spValModifier.spValModifierType.SubtractDNA)
                                {
                                    calcOption = DNAEvaluator.CalcOption.Subtract;
                                }

                                if (accessoryMod != null)
								{
									if (accessoryMod.modifier == spValModifier.spValModifierType.Multiply)
                                    {
                                        multiplier = accessoryMod.modifierValue;
                                    }
                                    else if (accessoryMod.modifier == spValModifier.spValModifierType.Divide)
                                    {
                                        multiplier = (1f / accessoryMod.modifierValue);
                                    }
                                    //otherwise we are stuffed- you can do add/subtract by using a different evaluator but I'm not gonna do that here
                                    else
                                    {
                                        multiplier = 1f;
                                    }
                                }
								_modifyingDNA.Add(new DNAEvaluator(modifiers[i].DNATypeName, DNAEvaluationGraph.Default, multiplier, calcOption));
							}
							if (accessoryMod != null)
                            {
                                i++;
                            }
                        }
					}
					_modifyingDNA.aggregationMethod = DNAEvaluatorList.AggregationMethodOpts.Cumulative;
					_modifiers.Clear();
				}
#pragma warning restore 618 //restore obsolete warning

				//Mark as obsolete
				//spValValue Special Types
				//The aim is to replace these with DNAEvaluators
				[Serializable]
				public class spValModifier
				{
					public enum spValModifierType { Add, Subtract, Multiply, Divide, AddDNA, SubtractDNA, MultiplyDNA, DivideDNA }
					[FormerlySerializedAs("modifier")]
					[SerializeField]
					private spValModifierType _modifier = spValModifierType.Add;

					[FormerlySerializedAs("DNATypeName")]
					[SerializeField]
					private string _DNATypeName = "";

					[FormerlySerializedAs("modifierValue")]
					[SerializeField]
					private float _modifierValue = 0f;

					public spValModifierType modifier
					{
						get { return _modifier; }
						set { _modifier = value; }
					}

					public string DNATypeName
					{
						get{return _DNATypeName;}
						set{_DNATypeName = value;}
					}

					public float modifierValue
					{
						get { return _modifierValue; }
						set { _modifierValue = value; }
					}

				}
			}
		}
	}
	
}
