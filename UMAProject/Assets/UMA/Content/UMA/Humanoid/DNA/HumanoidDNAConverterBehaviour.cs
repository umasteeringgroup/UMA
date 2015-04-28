using UnityEngine;
using System.Collections;

namespace UMA
{
	/// <summary>
	/// Base class for Humanoid DNA converters.
	/// </summary>
	/// <remarks>
	/// Holds hash values for all the bones used in the default UMA humanoid rig.
	/// </remarks>
	public class HumanoidDNAConverterBehaviour : DnaConverterBehaviour 
	{
		static bool builtHashes = false;
		static protected int headAdjustHash;
		static protected int neckAdjustHash;
		static protected int leftOuterBreastHash;
		static protected int rightOuterBreastHash;
		static protected int leftEyeHash;
		static protected int rightEyeHash;
		static protected int leftEyeAdjustHash;
		static protected int rightEyeAdjustHash;
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
		
		public override void Prepare()
		{
			if (builtHashes)
				return;
			
			headAdjustHash = UMAUtils.StringToHash("HeadAdjust");
			neckAdjustHash = UMAUtils.StringToHash("NeckAdjust");
			leftOuterBreastHash = UMAUtils.StringToHash("LeftOuterBreast");
			rightOuterBreastHash = UMAUtils.StringToHash("RightOuterBreast");
			leftEyeHash = UMAUtils.StringToHash("LeftEye");
			rightEyeHash = UMAUtils.StringToHash("RightEye");
			leftEyeAdjustHash = UMAUtils.StringToHash("LeftEyeAdjust");
			rightEyeAdjustHash = UMAUtils.StringToHash("RightEyeAdjust");
			spine1AdjustHash = UMAUtils.StringToHash("Spine1Adjust");
			spineAdjustHash = UMAUtils.StringToHash("SpineAdjust");
			lowerBackBellyHash = UMAUtils.StringToHash("LowerBackBelly");
			lowerBackAdjustHash = UMAUtils.StringToHash("LowerBackAdjust");
			leftTrapeziusHash = UMAUtils.StringToHash("LeftTrapezius");
			rightTrapeziusHash = UMAUtils.StringToHash("RightTrapezius");
			leftArmAdjustHash = UMAUtils.StringToHash("LeftArmAdjust");
			rightArmAdjustHash = UMAUtils.StringToHash("RightArmAdjust");
			leftForeArmAdjustHash = UMAUtils.StringToHash("LeftForeArmAdjust");
			rightForeArmAdjustHash = UMAUtils.StringToHash("RightForeArmAdjust");
			leftForeArmTwistAdjustHash = UMAUtils.StringToHash("LeftForeArmTwistAdjust");
			rightForeArmTwistAdjustHash = UMAUtils.StringToHash("RightForeArmTwistAdjust");
			leftShoulderAdjustHash = UMAUtils.StringToHash("LeftShoulderAdjust");
			rightShoulderAdjustHash = UMAUtils.StringToHash("RightShoulderAdjust");
			leftUpLegAdjustHash = UMAUtils.StringToHash("LeftUpLegAdjust");
			rightUpLegAdjustHash = UMAUtils.StringToHash("RightUpLegAdjust");
			leftLegAdjustHash = UMAUtils.StringToHash("LeftLegAdjust");
			rightLegAdjustHash = UMAUtils.StringToHash("RightLegAdjust");
			leftGluteusHash = UMAUtils.StringToHash("LeftGluteus");
			rightGluteusHash = UMAUtils.StringToHash("RightGluteus");
			leftEarAdjustHash = UMAUtils.StringToHash("LeftEarAdjust");
			rightEarAdjustHash = UMAUtils.StringToHash("RightEarAdjust");
			noseBaseAdjustHash = UMAUtils.StringToHash("NoseBaseAdjust");
			noseMiddleAdjustHash = UMAUtils.StringToHash("NoseMiddleAdjust");
			leftNoseAdjustHash = UMAUtils.StringToHash("LeftNoseAdjust");
			rightNoseAdjustHash = UMAUtils.StringToHash("RightNoseAdjust");
			upperLipsAdjustHash = UMAUtils.StringToHash("UpperLipsAdjust");
			mandibleAdjustHash = UMAUtils.StringToHash("MandibleAdjust");
			leftLowMaxilarAdjustHash = UMAUtils.StringToHash("LeftLowMaxilarAdjust");
			rightLowMaxilarAdjustHash = UMAUtils.StringToHash("RightLowMaxilarAdjust");
			leftCheekAdjustHash = UMAUtils.StringToHash("LeftCheekAdjust");
			rightCheekAdjustHash = UMAUtils.StringToHash("RightCheekAdjust");
			leftLowCheekAdjustHash = UMAUtils.StringToHash("LeftLowCheekAdjust");
			rightLowCheekAdjustHash = UMAUtils.StringToHash("RightLowCheekAdjust");
			noseTopAdjustHash = UMAUtils.StringToHash("NoseTopAdjust");
			leftEyebrowLowAdjustHash = UMAUtils.StringToHash("LeftEyebrowLowAdjust");
			rightEyebrowLowAdjustHash = UMAUtils.StringToHash("RightEyebrowLowAdjust");
			leftEyebrowMiddleAdjustHash = UMAUtils.StringToHash("LeftEyebrowMiddleAdjust");
			rightEyebrowMiddleAdjustHash = UMAUtils.StringToHash("RightEyebrowMiddleAdjust");
			leftEyebrowUpAdjustHash = UMAUtils.StringToHash("LeftEyebrowUpAdjust");
			rightEyebrowUpAdjustHash = UMAUtils.StringToHash("RightEyebrowUpAdjust");
			lipsSuperiorAdjustHash = UMAUtils.StringToHash("LipsSuperiorAdjust");
			lipsInferiorAdjustHash = UMAUtils.StringToHash("LipsInferiorAdjust");
			leftLipsSuperiorMiddleAdjustHash = UMAUtils.StringToHash("LeftLipsSuperiorMiddleAdjust");
			rightLipsSuperiorMiddleAdjustHash = UMAUtils.StringToHash("RightLipsSuperiorMiddleAdjust");
			leftLipsInferiorAdjustHash = UMAUtils.StringToHash("LeftLipsInferiorAdjust");
			rightLipsInferiorAdjustHash = UMAUtils.StringToHash("RightLipsInferiorAdjust");
			leftLipsAdjustHash = UMAUtils.StringToHash("LeftLipsAdjust");
			rightLipsAdjustHash = UMAUtils.StringToHash("RightLipsAdjust");
			globalHash = UMAUtils.StringToHash("Global");
			positionHash = UMAUtils.StringToHash("Position");
			lowerBackHash = UMAUtils.StringToHash("LowerBack");
			headHash = UMAUtils.StringToHash("Head");
			leftArmHash = UMAUtils.StringToHash("LeftArm");
			rightArmHash = UMAUtils.StringToHash("RightArm");
			leftForeArmHash = UMAUtils.StringToHash("LeftForeArm");
			rightForeArmHash = UMAUtils.StringToHash("RightForeArm");
			leftHandHash = UMAUtils.StringToHash("LeftHand");
			rightHandHash = UMAUtils.StringToHash("RightHand");
			leftFootHash = UMAUtils.StringToHash("LeftFoot");
			rightFootHash = UMAUtils.StringToHash("RightFoot");
			leftUpLegHash = UMAUtils.StringToHash("LeftUpLeg");
			rightUpLegHash = UMAUtils.StringToHash("RightUpLeg");
			leftShoulderHash = UMAUtils.StringToHash("LeftShoulder");
			rightShoulderHash = UMAUtils.StringToHash("RightShoulder");
			mandibleHash = UMAUtils.StringToHash("Mandible");
			
			builtHashes = true;
		}
	}
}
