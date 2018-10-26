using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;
using UMA.PoseTools;
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

		private float _masterWeight = 1f;

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

		public float masterWeight
		{
			set { _masterWeight = value; }
		}

		protected Dictionary<SkeletonPropType, Vector3> skelAddDefaults = new Dictionary<SkeletonPropType, Vector3>
			{
				{SkeletonPropType.Position, new Vector3(0f,-0.1f, 0.1f) },
				{SkeletonPropType.Rotation, new Vector3(0f,-360f, 360f) },
				{SkeletonPropType.Scale,  new Vector3(1f,0f, 5f) }
			};

		//These should be methods since they require the umaDNA and masterWeight values to be set/sent
		[Obsolete("Please use CalculateValueX((UMADnaBase umaDNA, float masterWeight) instead")]
		public Vector3 ValueX
		{
			get { return _valuesX.CalculateValue(_umaDNA, _masterWeight); }
		}
		[Obsolete("Please use CalculateValueY((UMADnaBase umaDNA, float masterWeight) instead")]
		public Vector3 ValueY
		{
			get { return _valuesY.CalculateValue(_umaDNA, _masterWeight); }
		}
		[Obsolete("Please use CalculateValueZ((UMADnaBase umaDNA, float masterWeight) instead")]
		public Vector3 ValueZ
		{
			get { return _valuesZ.CalculateValue(_umaDNA, _masterWeight); }
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

		public SkeletonModifier(SkeletonModifier importedModifier)
		{
			this._hashName = importedModifier._hashName;
			this._hash = importedModifier._hash;
			this._property = importedModifier._property;
			this._valuesX = importedModifier._valuesX;
			this._valuesY = importedModifier._valuesY;
			this._valuesZ = importedModifier._valuesZ;
			this._umaDNA = importedModifier._umaDNA;
		}

		public Vector3 CalculateValueX(UMADnaBase umaDNA, float masterWeight = 1f)
		{
			return _valuesX.CalculateValue(_umaDNA, masterWeight);
		}

		public Vector3 CalculateValueY(UMADnaBase umaDNA, float masterWeight = 1f)
		{
			return _valuesY.CalculateValue(_umaDNA, masterWeight);
		}

		public Vector3 CalculateValueZ(UMADnaBase umaDNA, float masterWeight = 1f)
		{
			return _valuesZ.CalculateValue(_umaDNA, masterWeight);
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

			public spVal(spVal importedSpVal)
			{
				_val = new spValValue();
				_val.value = importedSpVal._val.value;
				_val._modifiers = new List<spValValue.spValModifier>(importedSpVal._val.modifiers);
				_min = importedSpVal._min;
				_max = importedSpVal._max;
			}

			public Vector3 CalculateValue(UMADnaBase umaDNA, float masterWeight = 1f)
			{
				var thisVal = new Vector3();
				//val
				thisVal.x = _val.CalculateValue(umaDNA, masterWeight);
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
				public float _value = 0f;
				[FormerlySerializedAs("modifiers")]
				[SerializeField]
				public List<spValModifier> _modifiers = new List<spValModifier>();

				public float value
				{
					get { return _value; }
					set { _value = value; }
				}

				public List<spValModifier> modifiers
				{
					get { return _modifiers; }
					set { _modifiers = value; }
				}

				public float CalculateValue(UMADnaBase umaDNA, float masterWeight = 1f)
				{
					float thisVal = _value;
					float modifierVal = 0;
					float tempModifierVal = 0;
					string dnaCombineMethod = "";
					bool inModifierPair = false;
					if (_modifiers.Count > 0)
					{
						for (int i = 0; i < _modifiers.Count; i++)
						{
							if (_modifiers[i].DNATypeName != "None" && (_modifiers[i].modifier == spValModifier.spValModifierType.AddDNA ||
								_modifiers[i].modifier == spValModifier.spValModifierType.DivideDNA ||
								_modifiers[i].modifier == spValModifier.spValModifierType.MultiplyDNA ||
								_modifiers[i].modifier == spValModifier.spValModifierType.SubtractDNA))
							{
								tempModifierVal = GetUmaDNAValue(_modifiers[i].DNATypeName, umaDNA);
								tempModifierVal -= 0.5f;
								tempModifierVal = tempModifierVal * masterWeight;
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
									modifierVal += (tempModifierVal + (_modifiers[i].modifierValue * masterWeight));
									tempModifierVal = 0;
									inModifierPair = false;
								}
								else if (_modifiers[i].modifier == spValModifier.spValModifierType.Divide)
								{
									modifierVal += (tempModifierVal / (_modifiers[i].modifierValue * masterWeight));
									tempModifierVal = 0;
									inModifierPair = false;
								}
								else if (_modifiers[i].modifier == spValModifier.spValModifierType.Multiply)
								{
									modifierVal += (tempModifierVal * (_modifiers[i].modifierValue * masterWeight));
									tempModifierVal = 0;
									inModifierPair = false;
								}
								else if (_modifiers[i].modifier == spValModifier.spValModifierType.Subtract)
								{
									modifierVal += (tempModifierVal - (_modifiers[i].modifierValue * masterWeight));
									tempModifierVal = 0;
									inModifierPair = false;
								}
							}
							modifierVal = modifierVal * masterWeight;
							if (modifierVal != 0 && inModifierPair == false)
							{
								if (dnaCombineMethod == "Add")
								{
									thisVal += modifierVal;
								}
								if (dnaCombineMethod == "Subtract")
								{
									thisVal -= modifierVal;
								}
								if (dnaCombineMethod == "Multiply")
								{
									thisVal *= modifierVal;
								}
								if (dnaCombineMethod == "Divide")
								{
									thisVal /= modifierVal;
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
								thisVal += tempModifierVal;
							}
							if (dnaCombineMethod == "Subtract")
							{
								thisVal -= tempModifierVal;
							}
							if (dnaCombineMethod == "Multiply")
							{
								thisVal *= tempModifierVal;
							}
							if (dnaCombineMethod == "Divide")
							{
								thisVal /= tempModifierVal;
							}
							dnaCombineMethod = "";
							modifierVal = 0;
							tempModifierVal = 0;
							inModifierPair = false;
						}
					}
					return thisVal;
				}
				public float GetUmaDNAValue(string DNATypeName, UMADnaBase umaDnaIn)
				{
					if (umaDnaIn == null)
						return 0.5f;
					DynamicUMADnaBase umaDna = (DynamicUMADnaBase)umaDnaIn;
					float val = 0.5f;
					if (DNATypeName == "None" || umaDna == null)
					{
						return val;
					}
					val = umaDna.GetValue(DNATypeName, true);//implimented a 'failSilently' option here because recipes may have dna in that the dna asset no longer has
					return val;
				}

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
						get { return _DNATypeName; }
						set { _DNATypeName = value; }
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
