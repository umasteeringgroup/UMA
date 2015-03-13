using UnityEngine;
using System.Collections;


namespace UMA
{
	public class DnaConverterBehaviour : MonoBehaviour 
	{
	    public System.Type DNAType;
        public delegate void DNAConvertDelegate(UMAData data, UMASkeleton skeleton);
        public DNAConvertDelegate ApplyDnaAction;
	    public void ApplyDna(UMAData data, UMASkeleton skeleton)
	    {
	        ApplyDnaAction(data, skeleton);
	    }

		static bool builtHashes = false;
		static protected int headAdjustHash;
		static protected int neckAdjustHash;
		static protected int leftOuterBreastHash;
		static protected int rightOuterBreastHash;
		static protected int leftEyeHash;
		static protected int rightEyeHash;
		static protected int spine1AdjustHash;
		static protected int spineAdjustHash;
		static protected int lowerBackBellyHash;
		static protected int lowerBackAdjustHash;
		static protected int leftTrapeziusHash;
		static protected int rightTrapeziusHash;
		static protected int leftArmAdjustHash;
		static protected int rightArmAdjustHash;
		static protected int leftForeArmAdjustHash;
		static protected int rightForeArmAdjustHash;
		static protected int leftForeArmTwistAdjustHash;
		static protected int rightForeArmTwistAdjustHash;
		static protected int leftShoulderAdjustHash;
		static protected int rightShoulderAdjustHash;
		static protected int leftUpLegAdjustHash;
		static protected int rightUpLegAdjustHash;
		static protected int leftLegAdjustHash;
		static protected int rightLegAdjustHash;
		static protected int leftGluteusHash;
		static protected int rightGluteusHash;
		static protected int leftEarAdjustHash;
		static protected int rightEarAdjustHash;
		static protected int noseBaseAdjustHash;
		static protected int noseMiddleAdjustHash;
		static protected int leftNoseAdjustHash;
		static protected int rightNoseAdjustHash;
		static protected int upperLipsAdjustHash;
		static protected int mandibleAdjustHash;
		static protected int leftLowMaxilarAdjustHash;
		static protected int rightLowMaxilarAdjustHash;
		static protected int leftCheekAdjustHash;
		static protected int rightCheekAdjustHash;
		static protected int leftLowCheekAdjustHash;
		static protected int rightLowCheekAdjustHash;
		static protected int noseTopAdjustHash;
		static protected int leftEyebrowLowAdjustHash;
		static protected int rightEyebrowLowAdjustHash;
		static protected int leftEyebrowMiddleAdjustHash;
		static protected int rightEyebrowMiddleAdjustHash;
		static protected int leftEyebrowUpAdjustHash;
		static protected int rightEyebrowUpAdjustHash;
		static protected int lipsSuperiorAdjustHash;
		static protected int lipsInferiorAdjustHash;
		static protected int leftLipsSuperiorMiddleAdjustHash;
		static protected int rightLipsSuperiorMiddleAdjustHash;
		static protected int leftLipsInferiorAdjustHash;
		static protected int rightLipsInferiorAdjustHash;
		static protected int leftLipsAdjustHash;
		static protected int rightLipsAdjustHash;
		static protected int globalHash;
		static protected int positionHash;
		static protected int lowerBackHash;
		static protected int headHash;
		static protected int leftArmHash;
		static protected int rightArmHash;
		static protected int leftForeArmHash;
		static protected int rightForeArmHash;
		static protected int leftHandHash;
		static protected int rightHandHash;
		static protected int leftFootHash;
		static protected int rightFootHash;
		static protected int leftUpLegHash;
		static protected int rightUpLegHash;
		static protected int leftShoulderHash;
		static protected int rightShoulderHash;
		static protected int mandibleHash;
		
        public virtual void Prepare()
        {
			if (builtHashes)
				return;

			headAdjustHash = UMASkeleton.StringToHash("HeadAdjust");
			neckAdjustHash = UMASkeleton.StringToHash("NeckAdjust");
			leftOuterBreastHash = UMASkeleton.StringToHash("LeftOuterBreast");
			rightOuterBreastHash = UMASkeleton.StringToHash("RightOuterBreast");
			leftEyeHash = UMASkeleton.StringToHash("LeftEye");
			rightEyeHash = UMASkeleton.StringToHash("RightEye");
			spine1AdjustHash = UMASkeleton.StringToHash("Spine1Adjust");
			spineAdjustHash = UMASkeleton.StringToHash("SpineAdjust");
			lowerBackBellyHash = UMASkeleton.StringToHash("LowerBackBelly");
			lowerBackAdjustHash = UMASkeleton.StringToHash("LowerBackAdjust");
			leftTrapeziusHash = UMASkeleton.StringToHash("LeftTrapezius");
			rightTrapeziusHash = UMASkeleton.StringToHash("RightTrapezius");
			leftArmAdjustHash = UMASkeleton.StringToHash("LeftArmAdjust");
			rightArmAdjustHash = UMASkeleton.StringToHash("RightArmAdjust");
			leftForeArmAdjustHash = UMASkeleton.StringToHash("LeftForeArmAdjust");
			rightForeArmAdjustHash = UMASkeleton.StringToHash("RightForeArmAdjust");
			leftForeArmTwistAdjustHash = UMASkeleton.StringToHash("LeftForeArmTwistAdjust");
			rightForeArmTwistAdjustHash = UMASkeleton.StringToHash("RightForeArmTwistAdjust");
			leftShoulderAdjustHash = UMASkeleton.StringToHash("LeftShoulderAdjust");
			rightShoulderAdjustHash = UMASkeleton.StringToHash("RightShoulderAdjust");
			leftUpLegAdjustHash = UMASkeleton.StringToHash("LeftUpLegAdjust");
			rightUpLegAdjustHash = UMASkeleton.StringToHash("RightUpLegAdjust");
			leftLegAdjustHash = UMASkeleton.StringToHash("LeftLegAdjust");
			rightLegAdjustHash = UMASkeleton.StringToHash("RightLegAdjust");
			leftGluteusHash = UMASkeleton.StringToHash("LeftGluteus");
			rightGluteusHash = UMASkeleton.StringToHash("RightGluteus");
			leftEarAdjustHash = UMASkeleton.StringToHash("LeftEarAdjust");
			rightEarAdjustHash = UMASkeleton.StringToHash("RightEarAdjust");
			noseBaseAdjustHash = UMASkeleton.StringToHash("NoseBaseAdjust");
			noseMiddleAdjustHash = UMASkeleton.StringToHash("NoseMiddleAdjust");
			leftNoseAdjustHash = UMASkeleton.StringToHash("LeftNoseAdjust");
			rightNoseAdjustHash = UMASkeleton.StringToHash("RightNoseAdjust");
			upperLipsAdjustHash = UMASkeleton.StringToHash("UpperLipsAdjust");
			mandibleAdjustHash = UMASkeleton.StringToHash("MandibleAdjust");
			leftLowMaxilarAdjustHash = UMASkeleton.StringToHash("LeftLowMaxilarAdjust");
			rightLowMaxilarAdjustHash = UMASkeleton.StringToHash("RightLowMaxilarAdjust");
			leftCheekAdjustHash = UMASkeleton.StringToHash("LeftCheekAdjust");
			rightCheekAdjustHash = UMASkeleton.StringToHash("RightCheekAdjust");
			leftLowCheekAdjustHash = UMASkeleton.StringToHash("LeftLowCheekAdjust");
			rightLowCheekAdjustHash = UMASkeleton.StringToHash("RightLowCheekAdjust");
			noseTopAdjustHash = UMASkeleton.StringToHash("NoseTopAdjust");
			leftEyebrowLowAdjustHash = UMASkeleton.StringToHash("LeftEyebrowLowAdjust");
			rightEyebrowLowAdjustHash = UMASkeleton.StringToHash("RightEyebrowLowAdjust");
			leftEyebrowMiddleAdjustHash = UMASkeleton.StringToHash("LeftEyebrowMiddleAdjust");
			rightEyebrowMiddleAdjustHash = UMASkeleton.StringToHash("RightEyebrowMiddleAdjust");
			leftEyebrowUpAdjustHash = UMASkeleton.StringToHash("LeftEyebrowUpAdjust");
			rightEyebrowUpAdjustHash = UMASkeleton.StringToHash("RightEyebrowUpAdjust");
			lipsSuperiorAdjustHash = UMASkeleton.StringToHash("LipsSuperiorAdjust");
			lipsInferiorAdjustHash = UMASkeleton.StringToHash("LipsInferiorAdjust");
			leftLipsSuperiorMiddleAdjustHash = UMASkeleton.StringToHash("LeftLipsSuperiorMiddleAdjust");
			rightLipsSuperiorMiddleAdjustHash = UMASkeleton.StringToHash("RightLipsSuperiorMiddleAdjust");
			leftLipsInferiorAdjustHash = UMASkeleton.StringToHash("LeftLipsInferiorAdjust");
			rightLipsInferiorAdjustHash = UMASkeleton.StringToHash("RightLipsInferiorAdjust");
			leftLipsAdjustHash = UMASkeleton.StringToHash("LeftLipsAdjust");
			rightLipsAdjustHash = UMASkeleton.StringToHash("RightLipsAdjust");
			globalHash = UMASkeleton.StringToHash("Global");
			positionHash = UMASkeleton.StringToHash("Position");
			lowerBackHash = UMASkeleton.StringToHash("LowerBack");
			headHash = UMASkeleton.StringToHash("Head");
			leftArmHash = UMASkeleton.StringToHash("LeftArm");
			rightArmHash = UMASkeleton.StringToHash("RightArm");
			leftForeArmHash = UMASkeleton.StringToHash("LeftForeArm");
			rightForeArmHash = UMASkeleton.StringToHash("RightForeArm");
			leftHandHash = UMASkeleton.StringToHash("LeftHand");
			rightHandHash = UMASkeleton.StringToHash("RightHand");
			leftFootHash = UMASkeleton.StringToHash("LeftFoot");
			rightFootHash = UMASkeleton.StringToHash("RightFoot");
			leftUpLegHash = UMASkeleton.StringToHash("LeftUpLeg");
			rightUpLegHash = UMASkeleton.StringToHash("RightUpLeg");
			leftShoulderHash = UMASkeleton.StringToHash("LeftShoulder");
			rightShoulderHash = UMASkeleton.StringToHash("RightShoulder");
			mandibleHash = UMASkeleton.StringToHash("Mandible");

			builtHashes = true;
		}
    }
}