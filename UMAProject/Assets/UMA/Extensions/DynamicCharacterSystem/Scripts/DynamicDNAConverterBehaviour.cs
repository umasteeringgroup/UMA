using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UMA.PoseTools;

namespace UMA
{
    //A serialized version of DNAConverterBehaviour, so that we can include these settings in assetBundles, which cannot include their own scripts...
    //Uses DynamicUmaDna which can have Dynamic DNA Names based on a DynamicUmaDnaAsset - if there is no asset set DynamicUmaDna falls back to UMADnaHumanoid
    public class DynamicDNAConverterBehaviour : DynamicDNAConverterBehaviourBase
    {
        bool builtHashes = false;
		//the deafult list of BoneHashes based on UMADnaHumanoid but any UMA Bones can be added here and be modified by a Skeleton Modifier
		public List<HashListItem> hashList = new List<HashListItem>();

        public List<SkeletonModifier> skeletonModifiers = new List<SkeletonModifier>();

        public bool overallModifiersEnabled = true;
        public float overallScale = 1f;
        public Vector2 heightModifiers = new Vector2(0.85f, 1.2f);
        public float radiusModifier = 0.28f;
        public Vector3 massModifiers = new Vector3(46f, 26f, 26f);

        public UMABonePose startingPose = null;
        [Range(0f,1f)]
        [Tooltip("Adjust the influence the StartingPose has on the mesh. Tip: This can be animated to morph your character!")]
        public float startingPoseWeight = 1f;

        public DynamicDNAConverterBehaviour()
        {
            ApplyDnaAction = ApplyDynamicDnaAction;
            DNAType = typeof(DynamicUMADna);
        }

        public override void Prepare()
        {
            if (builtHashes)
                return;

            for (int i = 0; i < hashList.Count; i++)
            {
                hashList[i].hash = UMAUtils.StringToHash(hashList[i].hashName);
            }
            builtHashes = true;
        }

        /// <summary>
        /// Returns the set dnaTypeHash or generates one if not set
        /// </summary>
        /// <returns></returns>
        public override int DNATypeHash //we may not need this override since dnaTypeHash might never be 0
        {
			get
			{
				if (dnaAsset != null)
				{
					return dnaAsset.dnaTypeHash;
				}
				else
            	{
					Debug.LogWarning(this.name + " did not have a DNA Asset assigned. This is required for DynamicDnaConverters.");
            	}
            	return 0;
			}
        }

        public void ApplyDynamicDnaAction(UMAData umaData, UMASkeleton skeleton)
        {
            UpdateDynamicUMADnaBones(umaData, skeleton, false);
        }

        public void UpdateDynamicUMADnaBones(UMAData umaData, UMASkeleton skeleton, bool asReset = false)
        {
            UMADnaBase umaDna;
            //need to use the typehash
            umaDna = umaData.GetDna(DNATypeHash);

            if (umaDna == null || asReset == true)
            {
                umaDna = null;
            }
            //Make the DNAAssets match if they dont already...
            if(umaDna != null)
            if (((DynamicUMADnaBase)umaDna).dnaAsset != dnaAsset)
            {
                ((DynamicUMADnaBase)umaDna).dnaAsset = dnaAsset;
            }
            float overallScaleCalc = 0;
            float lowerBackScale = 0;
            bool overallScaleFound = false;
            bool lowerBackScaleFound = false;

            for (int i = 0; i < skeletonModifiers.Count; i++)
            {
                skeletonModifiers[i].umaDNA = umaDna;
                var thisHash = (skeletonModifiers[i].hash != 0) ? skeletonModifiers[i].hash : GetHash(skeletonModifiers[i].hashName);
                var thisValueX = skeletonModifiers[i].ValueX;
                var thisValueY = skeletonModifiers[i].ValueY;
                var thisValueZ = skeletonModifiers[i].ValueZ;
                if (skeletonModifiers[i].hashName == "Position" && skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Scale)
                {
                    var calcVal = thisValueX.x - skeletonModifiers[i].valuesX.val.value + overallScale;
                    overallScaleCalc = Mathf.Clamp(calcVal, thisValueX.y, thisValueX.z);
                    skeleton.SetScale(skeletonModifiers[i].hash, new Vector3(overallScaleCalc, overallScaleCalc, overallScaleCalc));
                    overallScaleFound = true;
                }
                else if (skeletonModifiers[i].hashName == "LowerBack" && skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Scale)
                {
                    lowerBackScale = Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z);
                    skeleton.SetScale(skeletonModifiers[i].hash, new Vector3(lowerBackScale, lowerBackScale, lowerBackScale));
                    lowerBackScaleFound = true;
                }
                else if (skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Position)
                {
                    skeleton.SetPositionRelative(thisHash,
                        new Vector3(
                            Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
                            Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
                            Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z)));
                }
                else if (skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Rotation)
                {
                    skeleton.SetRotationRelative(thisHash,
                        Quaternion.Euler(new Vector3(
                            Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
                            Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
                            Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z))),1f);
                }
                else if (skeletonModifiers[i].property == SkeletonModifier.SkeletonPropType.Scale)
                {
                    skeleton.SetScale(thisHash,
                        new Vector3(
                            Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
                            Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
                            Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z)));
                }

            }
            //overall modifiers
            if (overallScaleFound && lowerBackScaleFound && overallModifiersEnabled)
            {
                
                if(umaDna == null)
                {
                    umaData.characterHeight = overallScaleCalc * (heightModifiers.x + heightModifiers.y * lowerBackScale);
                    umaData.characterMass = massModifiers.x * overallScaleCalc + massModifiers.y + massModifiers.z;
                    umaData.characterRadius = radiusModifier * overallScaleCalc;
                }
                else
                {
                    umaData.characterHeight = overallScaleCalc * (heightModifiers.x + heightModifiers.y * lowerBackScale) + ((((DynamicUMADnaBase)umaDna).GetValue("feetSize",true) - 0.5f) * 0.20f);
                    umaData.characterMass = massModifiers.x * overallScaleCalc + massModifiers.y * ((DynamicUMADnaBase)umaDna).GetValue("upperWeight",true) + massModifiers.z * ((DynamicUMADnaBase)umaDna).GetValue("lowerWeight",true);
                    umaData.characterRadius = 0.24f + ((((DynamicUMADnaBase)umaDna).GetValue("height",true) - 0.5f) * 0.32f) + ((((DynamicUMADnaBase)umaDna).GetValue("upperMuscle",true) - 0.5f) * 0.01f);
                }
            }
            if (startingPose != null && asReset == false)
            {
                for (int i = 0; i < startingPose.poses.Length; i++)
                {
					skeleton.Morph(startingPose.poses[i].hash, startingPose.poses[i].position, startingPose.poses[i].scale, startingPose.poses[i].rotation, startingPoseWeight);
				}
            }
        }

        /// <summary>
        /// Method to temporarily remove any dna from a Skeleton. Useful for getting bone values for pre and post dna (since the skeletons own unmodified values often dont *quite* match for some reason- I think because a recipes 0.5 values end up as 0.5019608 when they come out of binary)
        /// Or it could be that I need to change the way this outputs values maybe?
        /// </summary>
        /// <param name="umaData"></param>
        public void RemoveDNAChangesFromSkeleton(UMAData umaData)
        {
            UpdateDynamicUMADnaBones(umaData, umaData.skeleton, true);
        }

        int GetHash(string hashName)
        {
            return hashList[hashList.FindIndex(s => s.hashName == hashName)].hash;
        }

		#region SPECIAL TYPES

		// a class for Bone hashes
		[Serializable]
        public class HashListItem
        {
            public string hashName = "";
            public int hash = 0;

			public HashListItem() { }

			public HashListItem(string nameToAdd)
			{
				hashName = nameToAdd;
				hash = UMAUtils.StringToHash(nameToAdd);
            }
			public HashListItem(string nameToAdd, int hashToAdd)
			{
				hashName = nameToAdd;
				var thisHash = hashToAdd;
				if(thisHash == 0)
					thisHash = UMAUtils.StringToHash(nameToAdd);
				hash = thisHash;
			}
		}

		//The main Skeleton Modifier Class- contains child classes for defining values
        [Serializable]
        public class SkeletonModifier
        {
			public enum SkeletonPropType { Position, Rotation, Scale }
			public string hashName;
			public int hash;
			public SkeletonPropType property = SkeletonPropType.Position;
			public spVal valuesX;
			public spVal valuesY;
			public spVal valuesZ;
			[HideInInspector]
			public UMADnaBase umaDNA;

			protected Dictionary<SkeletonPropType, Vector3> skelAddDefaults = new Dictionary<SkeletonPropType, Vector3>
			{
				{SkeletonPropType.Position, new Vector3(0f,-0.1f, 0.1f) },
				{SkeletonPropType.Rotation, new Vector3(0f,-360f, 360f) },
				{SkeletonPropType.Scale,  new Vector3(1f,0f, 5f) }
			};

			public Vector3 ValueX
			{
				get
				{
					return valuesX.CalculateValue(umaDNA);
				}
			}
			public Vector3 ValueY
			{
				get
				{
					return valuesY.CalculateValue(umaDNA);
				}
			}
			public Vector3 ValueZ
			{
				get
				{
					return valuesZ.CalculateValue(umaDNA);
				}
			}

			public SkeletonModifier() { }

			public SkeletonModifier(string _hashName, int _hash, SkeletonPropType _propType)
			{
				hashName = _hashName;
				hash = _hash;
				property = _propType;
				valuesX = new spVal(skelAddDefaults[_propType]);
				valuesY = new spVal(skelAddDefaults[_propType]);
				valuesZ = new spVal(skelAddDefaults[_propType]);
			}

			public SkeletonModifier(SkeletonModifier importedModifier)
			{
				hashName = importedModifier.hashName;
				hash = importedModifier.hash;
				property = importedModifier.property;
				valuesX = importedModifier.valuesX;
				valuesY = importedModifier.valuesY;
				valuesZ = importedModifier.valuesZ;
				umaDNA = importedModifier.umaDNA;
			}

			//Skeleton Modifier Special Types
			[Serializable]
            public class spVal
            {
				public spValValue val;
				public float min = 1;
				public float max = 1;

				public spVal() { }

				public spVal(Vector3 startingVals)
				{
					val = new spValValue();
					val.value = startingVals.x;
					min = startingVals.y;
					max = startingVals.z;
				}

				public Vector3 CalculateValue(UMADnaBase umaDNA)
				{
					var thisVal = new Vector3();
					//val
					thisVal.x = val.CalculateValue(umaDNA);
					//valmin
					thisVal.y = min;
					//max
					thisVal.z = max;
					return thisVal;
				}

				//spVal Special Types
				[Serializable]
                public class spValValue
                {
                    public float value = 0f;
                    public List<spValModifier> modifiers = new List<spValModifier>();

                    public float CalculateValue(UMADnaBase umaDNA)
                    {
                        float thisVal = value;
                        float modifierVal = 0;
                        float tempModifierVal = 0;
                        string dnaCombineMethod = "";
                        bool inModifierPair = false;
                        if (modifiers.Count > 0)
                        {
                            for (int i = 0; i < modifiers.Count; i++)
                            {
                                if (modifiers[i].DNATypeName != "None" && (modifiers[i].modifier == spValModifier.spValModifierType.AddDNA ||
                                    modifiers[i].modifier == spValModifier.spValModifierType.DivideDNA ||
                                    modifiers[i].modifier == spValModifier.spValModifierType.MultiplyDNA ||
                                    modifiers[i].modifier == spValModifier.spValModifierType.SubtractDNA))
                                {
                                    tempModifierVal = GetUmaDNAValue(modifiers[i].DNATypeName, umaDNA);
                                    tempModifierVal -= 0.5f;
                                    inModifierPair = true;
                                    if (modifiers[i].modifier == spValModifier.spValModifierType.AddDNA)
                                    {
                                        dnaCombineMethod = "Add";
                                    }
                                    else if (modifiers[i].modifier == spValModifier.spValModifierType.DivideDNA)
                                    {
                                        dnaCombineMethod = "Divide";
                                    }
                                    else if (modifiers[i].modifier == spValModifier.spValModifierType.MultiplyDNA)
                                    {
                                        dnaCombineMethod = "Multiply";
                                    }
                                    else if (modifiers[i].modifier == spValModifier.spValModifierType.SubtractDNA)
                                    {
                                        dnaCombineMethod = "Subtract";
                                    }
                                }
                                else
                                {
                                    if (modifiers[i].modifier == spValModifier.spValModifierType.Add)
                                    {
                                        modifierVal += (tempModifierVal + modifiers[i].modifierValue);
                                        tempModifierVal = 0;
                                        inModifierPair = false;
                                    }
                                    else if (modifiers[i].modifier == spValModifier.spValModifierType.Divide)
                                    {
                                        modifierVal += (tempModifierVal / modifiers[i].modifierValue);
                                        tempModifierVal = 0;
                                        inModifierPair = false;
                                    }
                                    else if (modifiers[i].modifier == spValModifier.spValModifierType.Multiply)
                                    {
                                        modifierVal += (tempModifierVal * modifiers[i].modifierValue);
                                        tempModifierVal = 0;
                                        inModifierPair = false;
                                    }
                                    else if (modifiers[i].modifier == spValModifier.spValModifierType.Subtract)
                                    {
                                        modifierVal += (tempModifierVal - modifiers[i].modifierValue);
                                        tempModifierVal = 0;
                                        inModifierPair = false;
                                    }
                                }
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
                        DynamicUMADnaBase umaDna = (DynamicUMADnaBase)umaDnaIn;
                        float val = 0.5f;
                        if (DNATypeName == "None" || umaDna == null)
                        {
                            return val;
                        }
						val = umaDna.GetValue(DNATypeName,true);//implimented a 'failSilently' option here- but that may not be a good idea?
                        return val;
                    }

					//spValValue Special Types
					[Serializable]
					public class spValModifier
					{
						public enum spValModifierType { Add, Subtract, Multiply, Divide, AddDNA, SubtractDNA, MultiplyDNA, DivideDNA }
						public spValModifierType modifier = spValModifierType.Add;
						public string DNATypeName = "";
						public float modifierValue = 0f;
					}
				}
            }
        }
		#endregion
	}
}
