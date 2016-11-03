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

        [Serializable]
        public class HashListItem
        {
            public string hashName = "";
            public int hash = 0;
        }
        [Serializable]
        public class SkeletonModifier
        {
            [Serializable]
            public class spVal
            {
                [Serializable]
                public class spValValue
                {
                    [Serializable]
                    public class spValModifier
                    {
                        public enum spValModifierType { Add, Subtract, Multiply, Divide, AddDNA, SubtractDNA, MultiplyDNA, DivideDNA }
                        public spValModifierType modifier = spValModifierType.Add;
                        public static string[] spValDNATypeFallback = new string[] //fallback to UMADnaHumanoid
                        {
                    "height",
                    "headSize",
                    "headWidth",
                    "neckThickness",
                    "armLength",
                    "forearmLength",
                    "armWidth",
                    "forearmWidth",
                    "handsSize",
                    "feetSize",
                    "legSeparation",
                    "upperMuscle",
                    "lowerMuscle",
                    "upperWeight",
                    "lowerWeight",
                    "legsSize",
                    "belly",
                    "waist",
                    "gluteusSize",
                    "earsSize",
                    "earsPosition",
                    "earsRotation",
                    "noseSize",
                    "noseCurve",
                    "noseWidth",
                    "noseInclination",
                    "nosePosition",
                    "nosePronounced",
                    "noseFlatten",
                    "chinSize",
                    "chinPronounced",
                    "chinPosition",
                    "mandibleSize",
                    "jawsSize",
                    "jawsPosition",
                    "cheekSize",
                    "cheekPosition",
                    "lowCheekPronounced",
                    "lowCheekPosition",
                    "foreheadSize",
                    "foreheadPosition",
                    "lipsSize",
                    "mouthSize",
                    "eyeRotation",
                    "eyeSize",
                    "breastSize"
                        };
                        public string DNATypeName = "";
                        public float modifierValue = 0f;
                    }
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
                        //try
                        //{
                            val = umaDna.GetValue(DNATypeName,true);//implimented a 'failSilently' option here- but that may not be a good idea?
                        //}
                        //catch
                        //{
                            //do we need the warning? The user might change the names and there is nothing wrong with that...
                            //Debug.LogWarning("No dna was found in DynamicUmaDna for " + DNATypeName);
                        //}
                        return val;
                    }
                }
                public spValValue val;
                public float min = 1;
                public float max = 1;

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
            }
            public enum SkeletonPropType { Position, Rotation, Scale }
            public string hashName;
            public int hash;
            public SkeletonPropType property = SkeletonPropType.Position;
            public spVal valuesX;
            public spVal valuesY;
            public spVal valuesZ;
            [HideInInspector]
            public UMADnaBase umaDNA;

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
        }

        bool builtHashes = false;
        //the deafult list of BoneHashes based on UMADnaHumanoid but any UMA Bones can be added here and be modified by a Skeleton Modifier
        public List<HashListItem> hashList = new List<HashListItem>{
        new HashListItem(){ hashName = "HeadAdjust" },
        new HashListItem(){ hashName = "NeckAdjust"},
        new HashListItem(){ hashName = "LeftOuterBreast"},
        new HashListItem(){ hashName = "RightOuterBreast"},
        new HashListItem(){ hashName = "LeftEye"},
        new HashListItem(){ hashName = "RightEye"},
        new HashListItem(){ hashName = "LeftEyeAdjust"},
        new HashListItem(){ hashName = "RightEyeAdjust"},
        new HashListItem(){ hashName = "Spine1Adjust"},
        new HashListItem(){ hashName = "SpineAdjust"},
        new HashListItem(){ hashName = "LowerBackBelly"},
        new HashListItem(){ hashName = "LowerBackAdjust"},
        new HashListItem(){ hashName = "LeftTrapezius"},
        new HashListItem(){ hashName = "RightTrapezius"},
        new HashListItem(){ hashName = "LeftArmAdjust"},
        new HashListItem(){ hashName = "RightArmAdjust"},
        new HashListItem(){ hashName = "LeftForeArmAdjust"},
        new HashListItem(){ hashName = "RightForeArmAdjust"},
        new HashListItem(){ hashName = "LeftForeArmTwistAdjust"},
        new HashListItem(){ hashName = "RightForeArmTwistAdjust"},
        new HashListItem(){ hashName = "LeftShoulderAdjust"},
        new HashListItem(){ hashName = "RightShoulderAdjust"},
        new HashListItem(){ hashName = "LeftUpLegAdjust"},
        new HashListItem(){ hashName = "RightUpLegAdjust"},
        new HashListItem(){ hashName = "LeftLegAdjust"},
        new HashListItem(){ hashName = "RightLegAdjust"},
        new HashListItem(){ hashName = "LeftGluteus"},
        new HashListItem(){ hashName = "RightGluteus"},
        new HashListItem(){ hashName = "LeftEarAdjust"},
        new HashListItem(){ hashName = "RightEarAdjust"},
        new HashListItem(){ hashName = "NoseBaseAdjust"},
        new HashListItem(){ hashName = "NoseMiddleAdjust"},
        new HashListItem(){ hashName = "LeftNoseAdjust"},
        new HashListItem(){ hashName = "RightNoseAdjust"},
        new HashListItem(){ hashName = "UpperLipsAdjust"},
        new HashListItem(){ hashName = "MandibleAdjust"},
        new HashListItem(){ hashName = "LeftLowMaxilarAdjust"},
        new HashListItem(){ hashName = "RightLowMaxilarAdjust"},
        new HashListItem(){ hashName = "LeftCheekAdjust"},
        new HashListItem(){ hashName = "RightCheekAdjust"},
        new HashListItem(){ hashName = "LeftLowCheekAdjust"},
        new HashListItem(){ hashName = "RightLowCheekAdjust"},
        new HashListItem(){ hashName = "NoseTopAdjust"},
        new HashListItem(){ hashName = "LeftEyebrowLowAdjust"},
        new HashListItem(){ hashName = "RightEyebrowLowAdjust"},
        new HashListItem(){ hashName = "LeftEyebrowMiddleAdjust"},
        new HashListItem(){ hashName = "RightEyebrowMiddleAdjust"},
        new HashListItem(){ hashName = "LeftEyebrowUpAdjust"},
        new HashListItem(){ hashName = "RightEyebrowUpAdjust"},
        new HashListItem(){ hashName = "LipsSuperiorAdjust"},
        new HashListItem(){ hashName = "LipsInferiorAdjust"},
        new HashListItem(){ hashName = "LeftLipsSuperiorMiddleAdjust"},
        new HashListItem(){ hashName = "RightLipsSuperiorMiddleAdjust"},
        new HashListItem(){ hashName = "LeftLipsInferiorAdjust"},
        new HashListItem(){ hashName = "RightLipsInferiorAdjust"},
        new HashListItem(){ hashName = "LeftLipsAdjust"},
        new HashListItem(){ hashName = "RightLipsAdjust"},
        new HashListItem(){ hashName = "Global"},
        new HashListItem(){ hashName = "Position"},
        new HashListItem(){ hashName = "LowerBack"},
        new HashListItem(){ hashName = "Head"},
        new HashListItem(){ hashName = "LeftArm"},
        new HashListItem(){ hashName = "RightArm"},
        new HashListItem(){ hashName = "LeftForeArm"},
        new HashListItem(){ hashName = "RightForeArm"},
        new HashListItem(){ hashName = "LeftHand"},
        new HashListItem(){ hashName = "RightHand"},
        new HashListItem(){ hashName = "LeftFoot"},
        new HashListItem(){ hashName = "RightFoot"},
        new HashListItem(){ hashName = "LeftUpLeg"},
        new HashListItem(){ hashName = "RightUpLeg"},
        new HashListItem(){ hashName = "LeftShoulder"},
        new HashListItem(){ hashName = "RightShoulder"},
        new HashListItem(){ hashName = "Mandible"}
    };

        public List<SkeletonModifier> skeletonModifiers = new List<SkeletonModifier>();

        public bool overallModifiersEnabled = true;
        public float overallScale = 1f;
        public Vector2 heightModifiers = new Vector2(0.85f, 1.2f);
        public float radiusModifier = 0.28f;
        public Vector3 massModifiers = new Vector3(46f, 26f, 26f);

        //[SerializeField]//TODO Check if this needs to be serialized- will it help with assetBundles?
        //public DynamicUMADnaAsset dnaAsset;

        public UMABonePose startingPose = null;
        [Range(0f,1f)]
        [Tooltip("Adjust the influence the StartingPose has on the mesh. Tip: This can be animated to morph your character!")]
        public float startingPoseWeight = 1f;

        //TODO Hide these in the inspector for release
        public string lastKnownAssetPath = "";
        public string lastKnownDuplicateAssetPath = "";
        //we need the instanceID because the user might change the name of the asset to BE the lastknownDuplicateAssetPath
        public int lastKnownInstanceID = 0;

        public DynamicDNAConverterBehaviour()
        {
            ApplyDnaAction = ApplyDynamicDnaAction;
            DNAType = typeof(DynamicUMADna);
        }

        public override void Prepare()
        {
            if (dnaTypeHash == 0)
            {
                dnaTypeHash = GenerateUniqueDnaTypeHash();
            }
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
        public override int GetDnaTypeHash()//we may not need this override since dnaTypeHash might never be 0
        {
            if(dnaTypeHash == 0)
            {
                dnaTypeHash = GenerateUniqueDnaTypeHash();
            }
            return dnaTypeHash;
        }

        public void ApplyDynamicDnaAction(UMAData umaData, UMASkeleton skeleton)
        {
            UpdateDynamicUMADnaBones(umaData, skeleton, false);
        }

        public void UpdateDynamicUMADnaBones(UMAData umaData, UMASkeleton skeleton, bool asReset = false)
        {
            UMADnaBase umaDna;
            //need to use the typehash
            umaDna = umaData.GetDna(GetDnaTypeHash());

            if (umaDna == null || asReset == true)
            {
                //umaDna = new DynamicUMADnaBase();
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
                    /*skeleton.SetRotation(thisHash,
                        Quaternion.Euler(new Vector3(
                            Mathf.Clamp(thisValueX.x, thisValueX.y, thisValueX.z),
                            Mathf.Clamp(thisValueY.x, thisValueY.y, thisValueY.z),
                            Mathf.Clamp(thisValueZ.x, thisValueZ.y, thisValueZ.z))));*/
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
                    umaData.characterHeight = overallScaleCalc * (heightModifiers.x + heightModifiers.y * lowerBackScale) + ((((DynamicUMADnaBase)umaDna).GetValue("feetSize") - 0.5f) * 0.20f);
                    umaData.characterMass = massModifiers.x * overallScaleCalc + massModifiers.y * ((DynamicUMADnaBase)umaDna).GetValue("upperWeight") + massModifiers.z * ((DynamicUMADnaBase)umaDna).GetValue("lowerWeight");
                    umaData.characterRadius = 0.24f + ((((DynamicUMADnaBase)umaDna).GetValue("height") - 0.5f) * 0.32f) + ((((DynamicUMADnaBase)umaDna).GetValue("upperMuscle") - 0.5f) * 0.01f);
                }
            }
            if (startingPose != null && asReset == false)
            {
                for (int i = 0; i < startingPose.poses.Length; i++)
                {
					/*skeleton.SetPositionRelative(startingPose.poses[i].hash, (startingPose.poses[i].position * startingPoseWeight));
                    var weightedScale = startingPose.poses[i].scale;
                    weightedScale.x = (weightedScale.x * startingPoseWeight) + (1 * (1 - startingPoseWeight));
                    weightedScale.y = (weightedScale.y * startingPoseWeight) + (1 * (1 - startingPoseWeight));
                    weightedScale.z = (weightedScale.z * startingPoseWeight) + (1 * (1 - startingPoseWeight));
                    skeleton.SetScaleRelative(startingPose.poses[i].hash, weightedScale);
                    skeleton.SetRotationRelative(startingPose.poses[i].hash, startingPose.poses[i].rotation, startingPoseWeight);*/
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

        #region File Handling

        public static int GenerateUniqueDnaTypeHash()
        {
            System.Random random = new System.Random();
            int i = random.Next();
            return i;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Method to update the UniqueHashID of a DynamicDNAConverterBehaviour when the user duplicates an asset. Not a full solution since what we really need is something that does this on Instantiate so that if an object is duplicated from a script the id gets updated
        /// </summary>
        public bool SetCurrentAssetPath()
        {
            bool DoUpdate = false;
            if (dnaTypeHash == 0)
            {
                dnaTypeHash = GenerateUniqueDnaTypeHash();
                DoUpdate = true;
            }
            var currentAssetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (lastKnownAssetPath == "" || lastKnownDuplicateAssetPath == "")//not sure if using 'this' makes any difference or not...
            {
                if (lastKnownAssetPath == "")
                {
                    lastKnownAssetPath = currentAssetPath;
                    DoUpdate = true;
                }
                if (this.lastKnownDuplicateAssetPath == "")
                {
                    lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
                    DoUpdate = true;
                }
            }
            else
            {
                //the user has changed the file name. Dont update the ID but DO update the lastKnown paths
                if (currentAssetPath != lastKnownAssetPath && currentAssetPath != lastKnownDuplicateAssetPath)
                {
                    lastKnownAssetPath = currentAssetPath;
                    lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
                    lastKnownInstanceID = GetInstanceID();
                    DoUpdate = true;
                }
                //The user has duplicated the file so update the paths AND the ID
                else if (currentAssetPath != lastKnownAssetPath && currentAssetPath == lastKnownDuplicateAssetPath)
                {
                    if (GetInstanceID() == lastKnownInstanceID)
                    {
                        lastKnownAssetPath = currentAssetPath;
                        lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
                    }
                    else
                    {
                        dnaTypeHash = GenerateUniqueDnaTypeHash();
                        lastKnownAssetPath = currentAssetPath;
                        lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
                        lastKnownInstanceID = GetInstanceID();
                    }
                    DoUpdate = true;
                }
                //Otherwise The following always needs to happen        
                else if (currentAssetPath == lastKnownAssetPath)
                {
                    var updatedlastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
                    if (updatedlastKnownDuplicateAssetPath != lastKnownDuplicateAssetPath)
                    {
                        lastKnownAssetPath = updatedlastKnownDuplicateAssetPath;
                        DoUpdate = true;
                    }
                    var updatedlastKnownInstanceID = GetInstanceID();
                    if (updatedlastKnownInstanceID != lastKnownInstanceID)
                    {
                        lastKnownInstanceID = updatedlastKnownInstanceID;
                        DoUpdate = true;
                    }
                }
            }
            return DoUpdate;
        }
#endif
        #endregion
    }
}
