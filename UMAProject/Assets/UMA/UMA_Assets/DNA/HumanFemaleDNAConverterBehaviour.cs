using UnityEngine;
using System.Collections;
using UMA;

public class HumanFemaleDNAConverterBehaviour : DnaConverterBehaviour
{
	public HumanFemaleDNAConverterBehaviour()
	{
		this.ApplyDnaAction = UpdateUMAFemaleDNABones;
		this.DNAType = typeof(UMADnaHumanoid);
	}
	
	static bool builtHashes = false;
	static int headAdjustHash;
	static int neckAdjustHash;
	static int leftOuterBreastHash;
	static int rightOuterBreastHash;
	static int leftEyeHash;
	static int rightEyeHash;
	static int spine1AdjustHash;
	static int spineAdjustHash;
	static int lowerBackBellyHash;
	static int lowerBackAdjustHash;
	static int leftTrapeziusHash;
	static int rightTrapeziusHash;
	static int leftArmAdjustHash;
	static int rightArmAdjustHash;
	static int leftForeArmAdjustHash;
	static int rightForeArmAdjustHash;
	static int leftForeArmTwistAdjustHash;
	static int rightForeArmTwistAdjustHash;
	static int leftShoulderAdjustHash;
	static int rightShoulderAdjustHash;
	static int leftUpLegAdjustHash;
	static int rightUpLegAdjustHash;
	static int leftLegAdjustHash;
	static int rightLegAdjustHash;
	static int leftGluteusHash;
	static int rightGluteusHash;
	static int leftEarAdjustHash;
	static int rightEarAdjustHash;
	static int noseBaseAdjustHash;
	static int noseMiddleAdjustHash;
	static int leftNoseAdjustHash;
	static int rightNoseAdjustHash;
	static int upperLipsAdjustHash;
	static int mandibleAdjustHash;
	static int leftLowMaxilarAdjustHash;
	static int rightLowMaxilarAdjustHash;
	static int leftCheekAdjustHash;
	static int rightCheekAdjustHash;
	static int leftLowCheekAdjustHash;
	static int rightLowCheekAdjustHash;
	static int noseTopAdjustHash;
	static int leftEyebrowLowAdjustHash;
	static int rightEyebrowLowAdjustHash;
	static int leftEyebrowMiddleAdjustHash;
	static int rightEyebrowMiddleAdjustHash;
	static int leftEyebrowUpAdjustHash;
	static int rightEyebrowUpAdjustHash;
	static int lipsSuperiorAdjustHash;
	static int lipsInferiorAdjustHash;
	static int leftLipsSuperiorMiddleAdjustHash;
	static int rightLipsSuperiorMiddleAdjustHash;
	static int leftLipsInferiorAdjustHash;
	static int rightLipsInferiorAdjustHash;
	static int leftLipsAdjustHash;
	static int rightLipsAdjustHash;
	static int globalHash;
	static int positionHash;
	static int lowerBackHash;
	static int headHash;
	static int leftArmHash;
	static int rightArmHash;
	static int leftForeArmHash;
	static int rightForeArmHash;
	static int leftHandHash;
	static int rightHandHash;
	static int leftFootHash;
	static int rightFootHash;
	static int leftUpLegHash;
	static int rightUpLegHash;
	static int leftShoulderHash;
	static int rightShoulderHash;
	static int mandibleHash;
	
	private static void buildHashes()
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
	
	
	public static void UpdateUMAFemaleDNABones(UMAData umaData, UMASkeleton skeleton)
	{
		buildHashes();
		var umaDna = umaData.GetDna<UMADnaHumanoid>();
		skeleton.SetScale(headAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 1, 1),
			Mathf.Clamp(1 + (umaDna.headWidth - 0.5f) * 0.30f, 0.5f, 1.6f),
			Mathf.Clamp(1 , 1, 1)));
		
		//umaData.boneList["HeadAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 1, 1),
		//Mathf.Clamp(1 + (umaDna.headWidth - 0.5f) * 0.30f, 0.5f, 1.6f),
		//Mathf.Clamp(1 , 1, 1));
		
		skeleton.SetScale(neckAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 0.80f, 0.5f, 1.6f),
			Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 1.2f, 0.5f, 1.6f)));
		
		//umaData.boneList["NeckAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 0.80f, 0.5f, 1.6f),
		//Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 1.2f, 0.5f, 1.6f));
		
		skeleton.SetScale(leftOuterBreastHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
			Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
			Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f)));
		skeleton.SetScale(rightOuterBreastHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
			Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
			Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f)));
		
		//umaData.boneList["LeftOuterBreast"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f));
		//umaData.boneList["RightOuterBreast"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f));
		
		skeleton.SetScale(leftEyeHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
			Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
			Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f)));
		skeleton.SetScale(rightEyeHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
			Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
			Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f)));
		
		//umaData.boneList["LeftEye"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
		//Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
		//Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f));
		//umaData.boneList["RightEye"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
		//Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
		//Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f));     
		
		skeleton.SetRotation(leftEyeHash,
		                     Quaternion.Euler(new Vector3((umaDna.eyeRotation - 0.5f) * 20, -90, -180)));
		skeleton.SetRotation(rightEyeHash,
		                     Quaternion.Euler(new Vector3(-(umaDna.eyeRotation - 0.5f) * 20, -90, -180)));
		
		//umaData.boneList["LeftEye"].boneTransform.localEulerAngles = new Vector3((umaDna.eyeRotation - 0.5f) * 20, -90, -180);
		//umaData.boneList["RightEye"].boneTransform.localEulerAngles = new Vector3(-(umaDna.eyeRotation - 0.5f) * 20, -90, -180);
		
		skeleton.SetScale(spine1AdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.15f, 0.75f, 1.10f),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.00f)));
		
		//umaData.boneList["Spine1Adjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.15f, 0.75f, 1.10f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.00f));
		
		skeleton.SetScale(spineAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.350f),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.450f)));
		
		//umaData.boneList["SpineAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.350f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.450f));
		
		skeleton.SetScale(lowerBackBellyHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 1.75f, 0.35f, 1.75f),
			Mathf.Clamp(1 + (umaDna.waist - 0.5f) * 1.75f, 0.35f, 1.75f),
			Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 3.00f, 0.35f, 3.0f)));
		
		//umaData.boneList["LowerBackBelly"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 1.75f, 0.35f, 1.75f),
		//Mathf.Clamp(1 + (umaDna.waist - 0.5f) * 1.75f, 0.35f, 1.75f),
		//Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 3.00f, 0.35f, 3.0f));
		
		skeleton.SetScale(lowerBackAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f)));
		
		//umaData.boneList["LowerBackAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f));
		
		skeleton.SetScale(leftTrapeziusHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
			Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
			Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f)));
		skeleton.SetScale(rightTrapeziusHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
			Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
			Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f)));
		
		//umaData.boneList["LeftTrapezius"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f));
		//umaData.boneList["RightTrapezius"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f));
		
		skeleton.SetScale(leftArmAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f),
			Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f)));
		skeleton.SetScale(rightArmAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f),
			Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f)));
		
		//umaData.boneList["LeftArmAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f),
		//Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f));
		//umaData.boneList["RightArmAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f),
		//Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f));
		
		skeleton.SetScale(leftForeArmAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f)));
		skeleton.SetScale(rightForeArmAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f)));
		
		//umaData.boneList["LeftForeArmAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f));
		//umaData.boneList["RightForeArmAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f));
		
		skeleton.SetScale(leftForeArmTwistAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f)));
		skeleton.SetScale(rightForeArmTwistAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f),
			Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f)));
		
		//umaData.boneList["LeftForeArmTwistAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f));
		//umaData.boneList["RightForeArmTwistAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f),
		//Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f));
		
		skeleton.SetScale(leftShoulderAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f)));
		skeleton.SetScale(rightShoulderAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f),
			Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f)));
		
		//umaData.boneList["LeftShoulderAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f));
		//umaData.boneList["RightShoulderAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f));
		
		skeleton.SetScale(leftUpLegAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f)));
		skeleton.SetScale(rightUpLegAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f)));
		
		//umaData.boneList["LeftUpLegAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f));
		//umaData.boneList["RightUpLegAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f));
		
		skeleton.SetScale(leftLegAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.95f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.75f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f)));
		skeleton.SetScale(rightLegAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1, 0.6f, 2),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.95f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f),
			Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.75f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f)));
		
		//umaData.boneList["LeftLegAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.95f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.75f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f));
		//umaData.boneList["RightLegAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.95f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f),
		//Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.75f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f));
		
		skeleton.SetScale(leftGluteusHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
			Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
			Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f)));
		skeleton.SetScale(rightGluteusHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
			Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
			Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f)));
		
		//umaData.boneList["LeftGluteus"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
		//Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
		//Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f));
		//umaData.boneList["RightGluteus"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
		//Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
		//Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f));
		
		skeleton.SetScale(leftEarAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
			Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
			Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f)));
		skeleton.SetScale(rightEarAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
			Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
			Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f)));
		
		//umaData.boneList["LeftEarAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f));
		//umaData.boneList["RightEarAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f));
		
		skeleton.SetPosition(leftEarAdjustHash,
		                     skeleton.GetPosition(leftEarAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
			Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.03f, -0.03f, 0.03f),
			Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f)));
		skeleton.SetPosition(rightEarAdjustHash,
		                     skeleton.GetPosition(rightEarAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
			Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * 0.03f, -0.03f, 0.03f),
			Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f)));
		
		skeleton.SetRotation(leftEarAdjustHash,
		                     Quaternion.Euler(new Vector3(
			Mathf.Clamp(0, -30, 80),
			Mathf.Clamp(0, -30, 80),
			Mathf.Clamp((umaDna.earsRotation - 0.5f) * 40, -15, 40))));
		skeleton.SetRotation(rightEarAdjustHash,
		                     Quaternion.Euler(new Vector3(
			Mathf.Clamp(0, -30, 80),
			Mathf.Clamp(0, -30, 80),
			Mathf.Clamp((umaDna.earsRotation - 0.5f) * -40, -40, 15))));
		
		skeleton.SetScale(noseBaseAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 1.5f, 0.4f, 3.0f),
			Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseWidth - 0.5f) * 1.0f, 0.25f, 3.0f),
			Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseFlatten - 0.5f) * 0.75f, 0.25f, 3.0f)));
		skeleton.SetScale(noseMiddleAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 1.9f + (umaDna.noseSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.25f + (umaDna.noseWidth - 0.5f) * 0.5f, 0.5f, 3.0f),
			Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.10f, 0.5f, 3.0f)));
		skeleton.SetRotation(noseBaseAdjustHash,
		                     Quaternion.Euler(new Vector3(
			Mathf.Clamp(0, -30, 80),
			Mathf.Clamp((umaDna.noseInclination - 0.5f) * 60, -60, 30),
			Mathf.Clamp(0, -30, 80))));
		skeleton.SetPosition(noseBaseAdjustHash,
		                     skeleton.GetPosition(noseBaseAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f)));
		skeleton.SetPosition(noseMiddleAdjustHash,
		                     skeleton.GetPosition(noseBaseAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.006f, -0.012f, 0.012f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.007f, -0.015f, 0.015f)));
		
		skeleton.SetPosition(leftNoseAdjustHash,
		                     skeleton.GetPosition(leftNoseAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f)));
		skeleton.SetPosition(rightNoseAdjustHash,
		                     skeleton.GetPosition(rightNoseAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f)));
		
		skeleton.SetPosition(upperLipsAdjustHash,
		                     skeleton.GetPosition(upperLipsAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0045f, -0.0045f, 0.0045f)));
		
		skeleton.SetScale(mandibleAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.chinPronounced - 0.5f) * 0.18f, 0.55f, 1.75f),
			Mathf.Clamp(1 + (umaDna.chinSize - 0.5f) * 1.3f, 0.75f, 1.3f),
			Mathf.Clamp(1, 0.4f, 1.5f)));
		skeleton.SetPosition(mandibleAdjustHash,
		                     skeleton.GetPosition(mandibleAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.0125f, 0.0125f),
			Mathf.Clamp(0, -0.0125f, 0.0125f),
			Mathf.Clamp(0 + (umaDna.chinPosition - 0.5f) * 0.0075f, -0.0075f, 0.0075f)));
		
		skeleton.SetPosition(leftLowMaxilarAdjustHash,
		                     skeleton.GetPosition(leftLowMaxilarAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * 0.025f, -0.025f, 0.025f),
			Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));
		skeleton.SetPosition(rightLowMaxilarAdjustHash,
		                     skeleton.GetPosition(rightLowMaxilarAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * -0.025f, -0.025f, 0.025f),
			Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));
		
		skeleton.SetScale(leftCheekAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f)));
		skeleton.SetScale(rightCheekAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f)));
		skeleton.SetPosition(leftCheekAdjustHash,
		                     skeleton.GetPosition(leftCheekAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));
		skeleton.SetPosition(rightCheekAdjustHash,
		                     skeleton.GetPosition(rightCheekAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));
		
		skeleton.SetPosition(leftLowCheekAdjustHash,
		                     skeleton.GetPosition(leftLowCheekAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f)));
		skeleton.SetPosition(rightLowCheekAdjustHash,
		                     skeleton.GetPosition(rightLowCheekAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f)));
		
		skeleton.SetPosition(noseTopAdjustHash,
		                     skeleton.GetPosition(noseTopAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.025f + (umaDna.foreheadSize - 0.5f) * -0.0015f, -0.015f, 0.0025f)));
		
		skeleton.SetPosition(leftEyebrowLowAdjustHash,
		                     skeleton.GetPosition(leftEyebrowLowAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f)));
		skeleton.SetPosition(leftEyebrowMiddleAdjustHash,
		                     skeleton.GetPosition(leftEyebrowMiddleAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f)));
		skeleton.SetPosition(leftEyebrowUpAdjustHash,
		                     skeleton.GetPosition(leftEyebrowUpAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f)));
		
		skeleton.SetPosition(rightEyebrowLowAdjustHash,
		                     skeleton.GetPosition(rightEyebrowLowAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f)));
		skeleton.SetPosition(rightEyebrowMiddleAdjustHash,
		                     skeleton.GetPosition(rightEyebrowMiddleAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f)));
		skeleton.SetPosition(rightEyebrowUpAdjustHash,
		                     skeleton.GetPosition(rightEyebrowUpAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f)));
		
		skeleton.SetScale(lipsSuperiorAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(lipsInferiorAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f)));
		
		skeleton.SetScale(leftLipsSuperiorMiddleAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(rightLipsSuperiorMiddleAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(leftLipsInferiorAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(rightLipsInferiorAdjustHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		
		skeleton.SetPosition(lipsInferiorAdjustHash,
		                     skeleton.GetPosition(lipsInferiorAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f)));
		
		skeleton.SetPosition(leftLipsAdjustHash,
		                     skeleton.GetPosition(leftLipsAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.03f, -0.02f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f)));
		skeleton.SetPosition(rightLipsAdjustHash,
		                     skeleton.GetPosition(rightLipsAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.03f, -0.005f, 0.02f),
			Mathf.Clamp(0, -0.05f, 0.05f)));
		
		skeleton.SetPosition(leftLipsSuperiorMiddleAdjustHash,
		                     skeleton.GetPosition(leftLipsSuperiorMiddleAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
			Mathf.Clamp(0, -0.05f, 0.05f)));
		skeleton.SetPosition(rightLipsSuperiorMiddleAdjustHash,
		                     skeleton.GetPosition(rightLipsSuperiorMiddleAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
			Mathf.Clamp(0, -0.05f, 0.05f)));
		skeleton.SetPosition(leftLipsInferiorAdjustHash,
		                     skeleton.GetPosition(leftLipsInferiorAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
			Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f)));
		skeleton.SetPosition(rightLipsInferiorAdjustHash,
		                     skeleton.GetPosition(rightLipsInferiorAdjustHash) +
		                     new Vector3(
			Mathf.Clamp(0, -0.05f, 0.05f),
			Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
			Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f)));
		
		
		////Bone structure change
		skeleton.SetScale(globalHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));
		
		skeleton.SetPosition(positionHash,
		                     skeleton.GetPosition(positionHash) +
		                     new Vector3(
			Mathf.Clamp((umaDna.feetSize - 0.5f) * -0.27f, -0.15f, 0.0675f),
			Mathf.Clamp(0, -10, 10),
			Mathf.Clamp(0, -10, 10)));
		
		skeleton.SetScale(lowerBackHash, 
		                  new Vector3(
			Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));
		
		skeleton.SetScale(headHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
			Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
			Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2)));
		
		skeleton.SetScale(leftArmHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(rightArmHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		
		skeleton.SetScale(leftForeArmHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(rightForeArmHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		
		skeleton.SetScale(leftHandHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(rightHandHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		
		skeleton.SetScale(leftFootHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(rightFootHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		
		skeleton.SetPosition(leftUpLegHash,
		                     skeleton.GetPosition(leftUpLegHash) +
		                     new Vector3(
			Mathf.Clamp(0, -10, 10),
			Mathf.Clamp((umaDna.legSeparation - 0.5f) * -0.15f + (umaDna.lowerWeight - 0.5f) * -0.035f + (umaDna.legsSize - 0.5f) * 0.1f, -0.055f, 0.055f),
			Mathf.Clamp(0, -10, 10)));
		skeleton.SetPosition(rightUpLegHash,
		                     skeleton.GetPosition(rightUpLegHash) +
		                     new Vector3(
			Mathf.Clamp(0, -10, 10),
			Mathf.Clamp((umaDna.legSeparation - 0.5f) * 0.15f + (umaDna.lowerWeight - 0.5f) * 0.035f + (umaDna.legsSize - 0.5f) * -0.1f, -0.025f, 0.025f),
			Mathf.Clamp(0, -10, 10)));
		
		skeleton.SetPosition(leftShoulderHash,
		                     skeleton.GetPosition(leftShoulderHash) +
		                     new Vector3(
			Mathf.Clamp(0, -10, 10),
			Mathf.Clamp((umaDna.upperMuscle - 0.5f) * -0.0235f, -0.025f, 0.015f),
			Mathf.Clamp(0, -10, 10)));
		skeleton.SetPosition(rightShoulderHash,
		                     skeleton.GetPosition(rightShoulderHash) +
		                     new Vector3(
			Mathf.Clamp(0, -10, 10),
			Mathf.Clamp((umaDna.upperMuscle - 0.5f) * 0.0235f, -0.015f, 0.025f),
			Mathf.Clamp(0, -10, 10)));
		
		skeleton.SetScale(mandibleHash, 
		                  new Vector3(
			Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
			Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
			Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f)));
	}
}
