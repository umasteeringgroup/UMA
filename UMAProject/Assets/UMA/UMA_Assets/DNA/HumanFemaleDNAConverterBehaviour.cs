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

    public static void UpdateUMAFemaleDNABones(UMAData umaData, UMASkeleton skeleton)
    {
        var umaDna = umaData.GetDna<UMADnaHumanoid>();
		skeleton.SetScale(UMASkeleton.StringToHash("HeadAdjust"), 
			new Vector3(
				Mathf.Clamp(1, 1, 1),
				Mathf.Clamp(1 + (umaDna.headWidth - 0.5f) * 0.30f, 0.5f, 1.6f),
				Mathf.Clamp(1 , 1, 1)));

		//umaData.boneList["HeadAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 1, 1),
		//Mathf.Clamp(1 + (umaDna.headWidth - 0.5f) * 0.30f, 0.5f, 1.6f),
		//Mathf.Clamp(1 , 1, 1));
		
		skeleton.SetScale(UMASkeleton.StringToHash("NeckAdjust"), 
			new Vector3(
				Mathf.Clamp(1, 0.6f, 2),
				Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 0.80f, 0.5f, 1.6f),
				Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 1.2f, 0.5f, 1.6f)));

		//umaData.boneList["NeckAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 0.80f, 0.5f, 1.6f),
		//Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 1.2f, 0.5f, 1.6f));
		
		skeleton.SetScale(UMASkeleton.StringToHash("LeftOuterBreast"), 
			new Vector3(
				Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
				Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
				Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightOuterBreast"), 
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
		
		skeleton.SetScale(UMASkeleton.StringToHash("LeftEye"), 
		    new Vector3(
				Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
				Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
				Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightEye"), 
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

		skeleton.SetRotation(UMASkeleton.StringToHash("LeftEye"),
			Quaternion.Euler(new Vector3((umaDna.eyeRotation - 0.5f) * 20, -90, -180)));
		skeleton.SetRotation(UMASkeleton.StringToHash("RightEye"),
			Quaternion.Euler(new Vector3(-(umaDna.eyeRotation - 0.5f) * 20, -90, -180)));

		//umaData.boneList["LeftEye"].boneTransform.localEulerAngles = new Vector3((umaDna.eyeRotation - 0.5f) * 20, -90, -180);
		//umaData.boneList["RightEye"].boneTransform.localEulerAngles = new Vector3(-(umaDna.eyeRotation - 0.5f) * 20, -90, -180);
		
		skeleton.SetScale(UMASkeleton.StringToHash("Spine1Adjust"), 
		    new Vector3(
				Mathf.Clamp(1, 0.6f, 2),
				Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.15f, 0.75f, 1.10f),
				Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.00f)));

		//umaData.boneList["Spine1Adjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.15f, 0.75f, 1.10f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.00f));

		skeleton.SetScale(UMASkeleton.StringToHash("SpineAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1, 0.6f, 2),
			  Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.350f),
			  Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.450f)));

		//umaData.boneList["SpineAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1, 0.6f, 2),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.350f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.450f));

		skeleton.SetScale(UMASkeleton.StringToHash("LowerBackBelly"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 1.75f, 0.35f, 1.75f),
			  Mathf.Clamp(1 + (umaDna.waist - 0.5f) * 1.75f, 0.35f, 1.75f),
			  Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 3.00f, 0.35f, 3.0f)));

		//umaData.boneList["LowerBackBelly"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 1.75f, 0.35f, 1.75f),
		//Mathf.Clamp(1 + (umaDna.waist - 0.5f) * 1.75f, 0.35f, 1.75f),
		//Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 3.00f, 0.35f, 3.0f));

		skeleton.SetScale(UMASkeleton.StringToHash("LowerBackAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f)));

		//umaData.boneList["LowerBackAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f));

		skeleton.SetScale(UMASkeleton.StringToHash("LeftTrapezius"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
			  Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
			  Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightTrapezius"), 
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

		skeleton.SetScale(UMASkeleton.StringToHash("LeftArmAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1, 0.6f, 2),
			  Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f),
			  Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightArmAdjust"), 
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

		skeleton.SetScale(UMASkeleton.StringToHash("LeftForeArmAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1, 0.6f, 2),
			  Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f),
			  Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightForeArmAdjust"), 
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

		skeleton.SetScale(UMASkeleton.StringToHash("LeftForeArmTwistAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1, 0.6f, 2),
			  Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f),
			  Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightForeArmTwistAdjust"), 
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

		skeleton.SetScale(UMASkeleton.StringToHash("LeftShoulderAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1, 0.6f, 2),
			  Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f),
			  Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightShoulderAdjust"), 
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

		skeleton.SetScale(UMASkeleton.StringToHash("LeftUpLegAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1, 0.6f, 2),
			  Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f),
			  Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightUpLegAdjust"), 
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

		skeleton.SetScale(UMASkeleton.StringToHash("LeftLegAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1, 0.6f, 2),
			  Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.95f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f),
			  Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.75f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightLegAdjust"), 
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
		
		skeleton.SetScale(UMASkeleton.StringToHash("LeftGluteus"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
			  Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
			  Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightGluteus"), 
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

		skeleton.SetScale(UMASkeleton.StringToHash("LeftEarAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightEarAdjust"), 
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

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftEarAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftEarAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
				Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.03f, -0.03f, 0.03f),
				Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightEarAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightEarAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
				Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * 0.03f, -0.03f, 0.03f),
				Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f)));

		//umaData.boneList["LeftEarAdjust"].boneTransform.localPosition = umaData.boneList["LeftEarAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
		//Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.03f, -0.03f, 0.03f),
		//Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f));
		//umaData.boneList["RightEarAdjust"].boneTransform.localPosition = umaData.boneList["LeftEarAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
		//Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * 0.03f, -0.03f, 0.03f),
		//Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f));

		skeleton.SetRotation(UMASkeleton.StringToHash("LeftEarAdjust"),
			Quaternion.Euler(new Vector3(
			  Mathf.Clamp(0, -30, 80),
			  Mathf.Clamp(0, -30, 80),
			  Mathf.Clamp((umaDna.earsRotation - 0.5f) * 40, -15, 40))));
		skeleton.SetRotation(UMASkeleton.StringToHash("RightEarAdjust"),
			Quaternion.Euler(new Vector3(
			  Mathf.Clamp(0, -30, 80),
			  Mathf.Clamp(0, -30, 80),
			  Mathf.Clamp((umaDna.earsRotation - 0.5f) * -40, -40, 15))));
		
		//umaData.boneList["LeftEarAdjust"].boneTransform.localEulerAngles = new Vector3(
		//Mathf.Clamp(0, -30, 80),
		//Mathf.Clamp(0, -30, 80),
		//Mathf.Clamp((umaDna.earsRotation - 0.5f) * 40, -15, 40));
		//umaData.boneList["RightEarAdjust"].boneTransform.localEulerAngles = new Vector3(
		//Mathf.Clamp(0, -30, 80),
		//Mathf.Clamp(0, -30, 80),
		//Mathf.Clamp((umaDna.earsRotation - 0.5f) * -40, -40, 15));

		skeleton.SetScale(UMASkeleton.StringToHash("NoseBaseAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 1.5f, 0.4f, 3.0f),
			  Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseWidth - 0.5f) * 1.0f, 0.25f, 3.0f),
			  Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseFlatten - 0.5f) * 0.75f, 0.25f, 3.0f)));
		skeleton.SetScale(UMASkeleton.StringToHash("NoseMiddleAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 1.9f + (umaDna.noseSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			  Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.25f + (umaDna.noseWidth - 0.5f) * 0.5f, 0.5f, 3.0f),
			  Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.10f, 0.5f, 3.0f)));
		skeleton.SetRotation(UMASkeleton.StringToHash("NoseBaseAdjust"),
			Quaternion.Euler(new Vector3(
			  Mathf.Clamp(0, -30, 80),
			  Mathf.Clamp((umaDna.noseInclination - 0.5f) * 60, -60, 30),
			  Mathf.Clamp(0, -30, 80))));
		skeleton.SetPosition(UMASkeleton.StringToHash("NoseBaseAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("NoseBaseAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("NoseMiddleAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("NoseBaseAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.006f, -0.012f, 0.012f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.007f, -0.015f, 0.015f)));

		//umaData.boneList["NoseBaseAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 1.5f, 0.4f, 3.0f),
		//Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseWidth - 0.5f) * 1.0f, 0.25f, 3.0f),
		//Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseFlatten - 0.5f) * 0.75f, 0.25f, 3.0f));
		//umaData.boneList["NoseMiddleAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 1.9f + (umaDna.noseSize - 0.5f) * 1.0f, 0.5f, 3.0f),
		//Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.25f + (umaDna.noseWidth - 0.5f) * 0.5f, 0.5f, 3.0f),
		//Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.10f, 0.5f, 3.0f));
		//umaData.boneList["NoseBaseAdjust"].boneTransform.localEulerAngles = new Vector3(
		//Mathf.Clamp(0, -30, 80),
		//Mathf.Clamp((umaDna.noseInclination - 0.5f) * 60, -60, 30),
		//Mathf.Clamp(0, -30, 80));
		//umaData.boneList["NoseBaseAdjust"].boneTransform.localPosition = umaData.boneList["NoseBaseAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f));
		//umaData.boneList["NoseMiddleAdjust"].boneTransform.localPosition = umaData.boneList["NoseBaseAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.006f, -0.012f, 0.012f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.007f, -0.015f, 0.015f));

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftNoseAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftNoseAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightNoseAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightNoseAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f)));

		//umaData.boneList["LeftNoseAdjust"].boneTransform.localPosition = umaData.boneList["LeftNoseAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f));
		//umaData.boneList["RightNoseAdjust"].boneTransform.localPosition = umaData.boneList["RightNoseAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f));

		skeleton.SetPosition(UMASkeleton.StringToHash("UpperLipsAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("UpperLipsAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0045f, -0.0045f, 0.0045f)));

		//umaData.boneList["UpperLipsAdjust"].boneTransform.localPosition = umaData.boneList["UpperLipsAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0045f, -0.0045f, 0.0045f));

		skeleton.SetScale(UMASkeleton.StringToHash("MandibleAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.chinPronounced - 0.5f) * 0.18f, 0.55f, 1.75f),
			  Mathf.Clamp(1 + (umaDna.chinSize - 0.5f) * 1.3f, 0.75f, 1.3f),
			  Mathf.Clamp(1, 0.4f, 1.5f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("MandibleAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("MandibleAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.0125f, 0.0125f),
				Mathf.Clamp(0, -0.0125f, 0.0125f),
				Mathf.Clamp(0 + (umaDna.chinPosition - 0.5f) * 0.0075f, -0.0075f, 0.0075f)));

		//umaData.boneList["MandibleAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.chinPronounced - 0.5f) * 0.18f, 0.55f, 1.75f),
		//Mathf.Clamp(1 + (umaDna.chinSize - 0.5f) * 1.3f, 0.75f, 1.3f),
		//Mathf.Clamp(1, 0.4f, 1.5f));
		//umaData.boneList["MandibleAdjust"].boneTransform.localPosition = umaData.boneList["MandibleAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.0125f, 0.0125f),
		//Mathf.Clamp(0, -0.0125f, 0.0125f),
		//Mathf.Clamp(0 + (umaDna.chinPosition - 0.5f) * 0.0075f, -0.0075f, 0.0075f));

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftLowMaxilarAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftLowMaxilarAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * 0.025f, -0.025f, 0.025f),
				Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightLowMaxilarAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightLowMaxilarAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * -0.025f, -0.025f, 0.025f),
				Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));

		//umaData.boneList["LeftLowMaxilarAdjust"].boneTransform.localPosition = umaData.boneList["LeftLowMaxilarAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * 0.025f, -0.025f, 0.025f),
		//Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f));
		//umaData.boneList["RightLowMaxilarAdjust"].boneTransform.localPosition = umaData.boneList["RightLowMaxilarAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * -0.025f, -0.025f, 0.025f),
		//Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f));

		skeleton.SetScale(UMASkeleton.StringToHash("LeftCheekAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			  Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			  Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightCheekAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			  Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
			  Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("LeftCheekAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftCheekAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightCheekAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightCheekAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f)));

		//umaData.boneList["LeftCheekAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
		//Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
		//Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f));
		//umaData.boneList["RightCheekAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
		//Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
		//Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f));
		//umaData.boneList["LeftCheekAdjust"].boneTransform.localPosition = umaData.boneList["LeftCheekAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f));
		//umaData.boneList["RightCheekAdjust"].boneTransform.localPosition = umaData.boneList["RightCheekAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f));

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftLowCheekAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftLowCheekAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightLowCheekAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightLowCheekAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f)));

		//umaData.boneList["LeftLowCheekAdjust"].boneTransform.localPosition = umaData.boneList["LeftLowCheekAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f));
		//umaData.boneList["RightLowCheekAdjust"].boneTransform.localPosition = umaData.boneList["RightLowCheekAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f));

		skeleton.SetPosition(UMASkeleton.StringToHash("NoseTopAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("NoseTopAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.025f + (umaDna.foreheadSize - 0.5f) * -0.0015f, -0.015f, 0.0025f)));

		//umaData.boneList["NoseTopAdjust"].boneTransform.localPosition = umaData.boneList["NoseTopAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.025f + (umaDna.foreheadSize - 0.5f) * -0.0015f, -0.015f, 0.0025f));

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftEyebrowLowAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftEyebrowLowAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("LeftEyebrowMiddleAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftEyebrowMiddleAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("LeftEyebrowUpAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftEyebrowUpAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f)));

		//umaData.boneList["LeftEyebrowLowAdjust"].boneTransform.localPosition = umaData.boneList["LeftEyebrowLowAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f));
		//umaData.boneList["LeftEyebrowMiddleAdjust"].boneTransform.localPosition = umaData.boneList["LeftEyebrowMiddleAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f));
		//umaData.boneList["LeftEyebrowUpAdjust"].boneTransform.localPosition = umaData.boneList["LeftEyebrowUpAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f));

		skeleton.SetPosition(UMASkeleton.StringToHash("RightEyebrowLowAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightEyebrowLowAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightEyebrowMiddleAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightEyebrowMiddleAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightEyebrowUpAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightEyebrowUpAdjust")) +
		  new Vector3(
				Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f)));

		//umaData.boneList["RightEyebrowLowAdjust"].boneTransform.localPosition = umaData.boneList["RightEyebrowLowAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f));
		//umaData.boneList["RightEyebrowMiddleAdjust"].boneTransform.localPosition = umaData.boneList["RightEyebrowMiddleAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f));
		//umaData.boneList["RightEyebrowUpAdjust"].boneTransform.localPosition = umaData.boneList["RightEyebrowUpAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f));
		
		skeleton.SetScale(UMASkeleton.StringToHash("LipsSuperiorAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(UMASkeleton.StringToHash("LipsInferiorAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f)));
		
		//umaData.boneList["LipsSuperiorAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
		//umaData.boneList["LipsInferiorAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f));

		skeleton.SetScale(UMASkeleton.StringToHash("LeftLipsSuperiorMiddleAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightLipsSuperiorMiddleAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(UMASkeleton.StringToHash("LeftLipsInferiorAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightLipsInferiorAdjust"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
			  Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f)));

		//umaData.boneList["LeftLipsSuperiorMiddleAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
		//umaData.boneList["RightLipsSuperiorMiddleAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
		//umaData.boneList["LeftLipsInferiorAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
		//umaData.boneList["RightLipsInferiorAdjust"].boneTransform.localScale = new Vector3(
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
		//Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));

		skeleton.SetPosition(UMASkeleton.StringToHash("LipsInferiorAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LipsInferiorAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f)));

		//umaData.boneList["LipsInferiorAdjust"].boneTransform.localPosition = umaData.boneList["LipsInferiorAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f));

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftLipsAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftLipsAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.03f, -0.02f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightLipsAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightLipsAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.03f, -0.005f, 0.02f),
				Mathf.Clamp(0, -0.05f, 0.05f)));

		//umaData.boneList["LeftLipsAdjust"].boneTransform.localPosition = umaData.boneList["LeftLipsAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.03f, -0.02f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f));
		//umaData.boneList["RightLipsAdjust"].boneTransform.localPosition = umaData.boneList["RightLipsAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.03f, -0.005f, 0.02f),
		//Mathf.Clamp(0, -0.05f, 0.05f));


		skeleton.SetPosition(UMASkeleton.StringToHash("LeftLipsSuperiorMiddleAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftLipsSuperiorMiddleAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
				Mathf.Clamp(0, -0.05f, 0.05f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightLipsSuperiorMiddleAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightLipsSuperiorMiddleAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
				Mathf.Clamp(0, -0.05f, 0.05f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("LeftLipsInferiorAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftLipsInferiorAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
				Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightLipsInferiorAdjust"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightLipsInferiorAdjust")) +
		  new Vector3(
				Mathf.Clamp(0, -0.05f, 0.05f),
				Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
				Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f)));

		//umaData.boneList["LeftLipsSuperiorMiddleAdjust"].boneTransform.localPosition = umaData.boneList["LeftLipsSuperiorMiddleAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
		//Mathf.Clamp(0, -0.05f, 0.05f));
		//umaData.boneList["RightLipsSuperiorMiddleAdjust"].boneTransform.localPosition = umaData.boneList["RightLipsSuperiorMiddleAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
		//Mathf.Clamp(0, -0.05f, 0.05f));
		//umaData.boneList["LeftLipsInferiorAdjust"].boneTransform.localPosition = umaData.boneList["LeftLipsInferiorAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
		//Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f));
		//umaData.boneList["RightLipsInferiorAdjust"].boneTransform.localPosition = umaData.boneList["RightLipsInferiorAdjust"].actualBonePosition + new Vector3(
		//Mathf.Clamp(0, -0.05f, 0.05f),
		//Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
		//Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f));


		////Bone structure change
		skeleton.SetScale(UMASkeleton.StringToHash("Global"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			  Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			  Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));

		//umaData.ChangeBoneScale("Global",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
		//Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
		//Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));

		skeleton.SetPosition(UMASkeleton.StringToHash("Position"),
			skeleton.GetPosition(UMASkeleton.StringToHash("Position")) +
		  new Vector3(
				Mathf.Clamp((umaDna.feetSize - 0.5f) * -0.27f, -0.15f, 0.0675f),
				Mathf.Clamp(0, -10, 10),
				Mathf.Clamp(0, -10, 10)));

		//umaData.ChangeBoneMoveRelative("Position", new Vector3(
		//Mathf.Clamp((umaDna.feetSize - 0.5f) * -0.27f, -0.15f, 0.0675f),
		//Mathf.Clamp(0, -10, 10),
		//Mathf.Clamp(0, -10, 10)));

		skeleton.SetScale(UMASkeleton.StringToHash("LowerBack"), 
		  new Vector3(
			  Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			  Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
			  Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));

		//umaData.ChangeBoneScale("LowerBack",
		//new Vector3(
		//Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
		//Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
		//Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));

		skeleton.SetScale(UMASkeleton.StringToHash("Head"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
			  Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
			  Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2)));

		//umaData.ChangeBoneScale("Head",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
		//Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
		//Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2)));

		skeleton.SetScale(UMASkeleton.StringToHash("LeftArm"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightArm"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));

		//umaData.ChangeBoneScale("LeftArm",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		//umaData.ChangeBoneScale("RightArm",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		
		skeleton.SetScale(UMASkeleton.StringToHash("LeftForeArm"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightForeArm"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));

		//umaData.ChangeBoneScale("LeftForeArm",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		//umaData.ChangeBoneScale("RightForeArm",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));

		skeleton.SetScale(UMASkeleton.StringToHash("LeftHand"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightHand"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));

		//umaData.ChangeBoneScale("LeftHand",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		//umaData.ChangeBoneScale("RightHand",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));

		skeleton.SetScale(UMASkeleton.StringToHash("LeftFoot"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		skeleton.SetScale(UMASkeleton.StringToHash("RightFoot"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
			  Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));

		//umaData.ChangeBoneScale("LeftFoot",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
		//umaData.ChangeBoneScale("RightFoot",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
		//Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));		
		
		skeleton.SetPosition(UMASkeleton.StringToHash("LeftUpLeg"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftUpLeg")) +
		  new Vector3(
				Mathf.Clamp(0, -10, 10),
				Mathf.Clamp((umaDna.legSeparation - 0.5f) * -0.15f + (umaDna.lowerWeight - 0.5f) * -0.035f + (umaDna.legsSize - 0.5f) * 0.1f, -0.055f, 0.055f),
				Mathf.Clamp(0, -10, 10)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightUpLeg"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightUpLeg")) +
		  new Vector3(
				Mathf.Clamp(0, -10, 10),
				Mathf.Clamp((umaDna.legSeparation - 0.5f) * 0.15f + (umaDna.lowerWeight - 0.5f) * 0.035f + (umaDna.legsSize - 0.5f) * -0.1f, -0.025f, 0.025f),
				Mathf.Clamp(0, -10, 10)));
				
		//umaData.ChangeBoneMoveRelative("LeftUpLeg",new Vector3(
		//Mathf.Clamp(0, -10, 10),
		//Mathf.Clamp((umaDna.legSeparation - 0.5f) * -0.15f + (umaDna.lowerWeight - 0.5f) * -0.035f + (umaDna.legsSize - 0.5f) * 0.1f, -0.055f, 0.055f),
		//Mathf.Clamp(0, -10, 10)));
		//umaData.ChangeBoneMoveRelative("RightUpLeg", new Vector3(
		//Mathf.Clamp(0, -10, 10),
		//Mathf.Clamp((umaDna.legSeparation - 0.5f) * 0.15f + (umaDna.lowerWeight - 0.5f) * 0.035f + (umaDna.legsSize - 0.5f) * -0.1f, -0.025f, 0.025f),
		//Mathf.Clamp(0, -10, 10)));

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftShoulder"),
			skeleton.GetPosition(UMASkeleton.StringToHash("LeftShoulder")) +
		  new Vector3(
				Mathf.Clamp(0, -10, 10),
				Mathf.Clamp((umaDna.upperMuscle - 0.5f) * -0.0235f, -0.025f, 0.015f),
				Mathf.Clamp(0, -10, 10)));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightShoulder"),
			skeleton.GetPosition(UMASkeleton.StringToHash("RightShoulder")) +
		  new Vector3(
				Mathf.Clamp(0, -10, 10),
				Mathf.Clamp((umaDna.upperMuscle - 0.5f) * 0.0235f, -0.015f, 0.025f),
				Mathf.Clamp(0, -10, 10)));

		//umaData.ChangeBoneMoveRelative("LeftShoulder",new Vector3(
		//Mathf.Clamp(0, -10, 10),
		//Mathf.Clamp((umaDna.upperMuscle - 0.5f) * -0.0235f, -0.025f, 0.015f),
		//Mathf.Clamp(0, -10, 10)));
		//umaData.ChangeBoneMoveRelative("RightShoulder", new Vector3(
		//Mathf.Clamp(0, -10, 10),
		//Mathf.Clamp((umaDna.upperMuscle - 0.5f) * 0.0235f, -0.015f, 0.025f),
		//Mathf.Clamp(0, -10, 10)));

		skeleton.SetScale(UMASkeleton.StringToHash("Mandible"), 
		  new Vector3(
			  Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
			  Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
			  Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f)));

		//umaData.ChangeBoneScale("Mandible",
		//new Vector3(
		//Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
		//Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f)));
    }


}
