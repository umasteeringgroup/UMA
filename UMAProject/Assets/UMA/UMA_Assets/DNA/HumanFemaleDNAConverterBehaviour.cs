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

    public static void UpdateUMAFemaleDNABones(UMAData umaData)
    {
        var umaDna = umaData.GetDna<UMADnaHumanoid>();

        //Bone adjust Change
		umaData.boneList["HeadAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 1, 1),
        Mathf.Clamp(1 + (umaDna.headWidth - 0.5f) * 0.30f, 0.5f, 1.6f),
        Mathf.Clamp(1 , 1, 1));
		
		umaData.boneList["NeckAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 0.80f, 0.5f, 1.6f),
        Mathf.Clamp(1 + (umaDna.neckThickness - 0.5f) * 1.2f, 0.5f, 1.6f));
		
        umaData.boneList["LeftOuterBreast"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
        Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
        Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f));
        umaData.boneList["RightOuterBreast"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
        Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f),
        Mathf.Clamp(1 + (umaDna.breastSize - 0.5f) * 1.50f + (umaDna.upperWeight - 0.5f) * 0.10f, 0.6f, 1.5f));
		
		
		umaData.boneList["LeftEye"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
        Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
        Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f));
		umaData.boneList["RightEye"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
        Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f),
        Mathf.Clamp(1 + (umaDna.eyeSize - 0.5f) * 0.3f , 0.7f, 1.4f));     

		umaData.boneList["LeftEye"].boneTransform.localEulerAngles = new Vector3((umaDna.eyeRotation - 0.5f) * 20, -90, -180);
        umaData.boneList["RightEye"].boneTransform.localEulerAngles = new Vector3(-(umaDna.eyeRotation - 0.5f) * 20, -90, -180);
		
        umaData.boneList["Spine1Adjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.15f, 0.75f, 1.10f),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.10f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.00f));

        umaData.boneList["SpineAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.350f),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.upperMuscle - 0.5f) * 0.25f, 0.85f, 1.450f));

        umaData.boneList["LowerBackBelly"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 1.75f, 0.35f, 1.75f),
        Mathf.Clamp(1 + (umaDna.waist - 0.5f) * 1.75f, 0.35f, 1.75f),
        Mathf.Clamp(1 + (umaDna.belly - 0.5f) * 3.00f, 0.35f, 3.0f));

        umaData.boneList["LowerBackAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.25f + (umaDna.lowerWeight - 0.5f) * 0.15f, 0.85f, 1.5f));

        umaData.boneList["LeftTrapezius"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
        Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
        Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f));
        umaData.boneList["RightTrapezius"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
        Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f),
        Mathf.Clamp(1 + (umaDna.upperMuscle - 0.5f) * 1.35f, 0.65f, 1.35f));

        umaData.boneList["LeftArmAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f),
        Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f));
        umaData.boneList["RightArmAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f),
        Mathf.Clamp(1 + (umaDna.armWidth - 0.5f) * 0.65f, 0.65f, 1.65f));

        umaData.boneList["LeftForeArmAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f),
        Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f));
        umaData.boneList["RightForeArmAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f),
        Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.65f, 0.75f, 1.25f));

        umaData.boneList["LeftForeArmTwistAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f),
        Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f));
        umaData.boneList["RightForeArmTwistAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
		Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f),
        Mathf.Clamp(1 + (umaDna.forearmWidth - 0.5f) * 0.35f, 0.75f, 1.25f));

        umaData.boneList["LeftShoulderAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f));
        umaData.boneList["RightShoulderAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f),
        Mathf.Clamp(1 + (umaDna.upperWeight - 0.5f) * 0.35f + (umaDna.upperMuscle - 0.5f) * 0.55f, 0.75f, 1.25f));

        umaData.boneList["LeftUpLegAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f));
        umaData.boneList["RightUpLegAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.65f + (umaDna.lowerMuscle - 0.5f) * 0.15f - (umaDna.legsSize - 0.5f), 0.45f, 1.35f));

        umaData.boneList["LeftLegAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.95f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.75f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f));
        umaData.boneList["RightLegAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1, 0.6f, 2),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.95f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f),
        Mathf.Clamp(1 + (umaDna.lowerWeight - 0.5f) * 0.15f + (umaDna.lowerMuscle - 0.5f) * 0.75f - (umaDna.legsSize - 0.5f), 0.65f, 1.45f));
		
		
		umaData.boneList["LeftGluteus"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
        Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
        Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f));
        umaData.boneList["RightGluteus"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
        Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f),
        Mathf.Clamp(1 + (umaDna.gluteusSize - 0.5f) * 1.35f , 0.25f, 2.35f));


        umaData.boneList["LeftEarAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
        Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
        Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f));
        umaData.boneList["RightEarAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
        Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f),
        Mathf.Clamp(1 + (umaDna.earsSize - 0.5f) * 1.0f, 0.75f, 1.5f));
		
        umaData.boneList["LeftEarAdjust"].boneTransform.localPosition = umaData.boneList["LeftEarAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
		Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.03f, -0.03f, 0.03f),
        Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f));
        umaData.boneList["RightEarAdjust"].boneTransform.localPosition = umaData.boneList["LeftEarAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * -0.01f, -0.01f, 0.01f),
		Mathf.Clamp(0 + (umaDna.headWidth - 0.5f) * 0.03f, -0.03f, 0.03f),
        Mathf.Clamp(0 + (umaDna.earsPosition - 0.5f) * 0.02f, -0.02f, 0.02f));

		
        umaData.boneList["LeftEarAdjust"].boneTransform.localEulerAngles = new Vector3(
        Mathf.Clamp(0, -30, 80),
        Mathf.Clamp(0, -30, 80),
        Mathf.Clamp((umaDna.earsRotation - 0.5f) * 40, -15, 40));
        umaData.boneList["RightEarAdjust"].boneTransform.localEulerAngles = new Vector3(
        Mathf.Clamp(0, -30, 80),
        Mathf.Clamp(0, -30, 80),
        Mathf.Clamp((umaDna.earsRotation - 0.5f) * -40, -40, 15));

        umaData.boneList["NoseBaseAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 1.5f, 0.4f, 3.0f),
        Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseWidth - 0.5f) * 1.0f, 0.25f, 3.0f),
        Mathf.Clamp(1 + (umaDna.noseSize - 0.5f) * 0.15f + (umaDna.noseFlatten - 0.5f) * 0.75f, 0.25f, 3.0f));
        umaData.boneList["NoseMiddleAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 1.9f + (umaDna.noseSize - 0.5f) * 1.0f, 0.5f, 3.0f),
        Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.25f + (umaDna.noseWidth - 0.5f) * 0.5f, 0.5f, 3.0f),
        Mathf.Clamp(1 + (umaDna.noseCurve - 0.5f) * 0.15f + (umaDna.noseSize - 0.5f) * 0.10f, 0.5f, 3.0f));
        umaData.boneList["NoseBaseAdjust"].boneTransform.localEulerAngles = new Vector3(
        Mathf.Clamp(0, -30, 80),
        Mathf.Clamp((umaDna.noseInclination - 0.5f) * 60, -60, 30),
        Mathf.Clamp(0, -30, 80));
        umaData.boneList["NoseBaseAdjust"].boneTransform.localPosition = umaData.boneList["NoseBaseAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f));
        umaData.boneList["NoseMiddleAdjust"].boneTransform.localPosition = umaData.boneList["NoseBaseAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.006f, -0.012f, 0.012f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.007f, -0.015f, 0.015f));

        umaData.boneList["LeftNoseAdjust"].boneTransform.localPosition = umaData.boneList["LeftNoseAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f));
        umaData.boneList["RightNoseAdjust"].boneTransform.localPosition = umaData.boneList["RightNoseAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.nosePronounced - 0.5f) * -0.0125f, -0.025f, 0.025f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0125f, -0.025f, 0.025f));


        umaData.boneList["UpperLipsAdjust"].boneTransform.localPosition = umaData.boneList["UpperLipsAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.nosePosition - 0.5f) * 0.0045f, -0.0045f, 0.0045f));


        umaData.boneList["MandibleAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.chinPronounced - 0.5f) * 0.18f, 0.55f, 1.75f),
        Mathf.Clamp(1 + (umaDna.chinSize - 0.5f) * 1.3f, 0.75f, 1.3f),
        Mathf.Clamp(1, 0.4f, 1.5f));
        umaData.boneList["MandibleAdjust"].boneTransform.localPosition = umaData.boneList["MandibleAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.0125f, 0.0125f),
        Mathf.Clamp(0, -0.0125f, 0.0125f),
        Mathf.Clamp(0 + (umaDna.chinPosition - 0.5f) * 0.0075f, -0.0075f, 0.0075f));

        umaData.boneList["LeftLowMaxilarAdjust"].boneTransform.localPosition = umaData.boneList["LeftLowMaxilarAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * 0.025f, -0.025f, 0.025f),
        Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f));
        umaData.boneList["RightLowMaxilarAdjust"].boneTransform.localPosition = umaData.boneList["RightLowMaxilarAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.jawsSize - 0.5f) * -0.025f, -0.025f, 0.025f),
        Mathf.Clamp(0 + (umaDna.jawsPosition - 0.5f) * 0.03f, -0.03f, 0.03f));

        umaData.boneList["LeftCheekAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
        Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
        Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f));
        umaData.boneList["RightCheekAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
        Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f),
        Mathf.Clamp(1 + (umaDna.cheekSize - 0.5f) * 1.05f, 0.35f, 2.05f));
        umaData.boneList["LeftCheekAdjust"].boneTransform.localPosition = umaData.boneList["LeftCheekAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f));
        umaData.boneList["RightCheekAdjust"].boneTransform.localPosition = umaData.boneList["RightCheekAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.cheekPosition - 0.5f) * 0.03f, -0.03f, 0.03f));


        umaData.boneList["LeftLowCheekAdjust"].boneTransform.localPosition = umaData.boneList["LeftLowCheekAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f));
        umaData.boneList["RightLowCheekAdjust"].boneTransform.localPosition = umaData.boneList["RightLowCheekAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.lowCheekPronounced - 0.5f) * -0.035f, -0.07f, 0.035f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.lowCheekPosition - 0.5f) * 0.06f, -0.06f, 0.06f));

        umaData.boneList["NoseTopAdjust"].boneTransform.localPosition = umaData.boneList["NoseTopAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.025f + (umaDna.foreheadSize - 0.5f) * -0.0015f, -0.015f, 0.0025f));

		umaData.boneList["LeftEyebrowLowAdjust"].boneTransform.localPosition = umaData.boneList["LeftEyebrowLowAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f));
        umaData.boneList["LeftEyebrowMiddleAdjust"].boneTransform.localPosition = umaData.boneList["LeftEyebrowMiddleAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f));
        umaData.boneList["LeftEyebrowUpAdjust"].boneTransform.localPosition = umaData.boneList["LeftEyebrowUpAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f));

        umaData.boneList["RightEyebrowLowAdjust"].boneTransform.localPosition = umaData.boneList["RightEyebrowLowAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.02f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.015f, 0.005f));
        umaData.boneList["RightEyebrowMiddleAdjust"].boneTransform.localPosition = umaData.boneList["RightEyebrowMiddleAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.04f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.025f, 0.005f));
        umaData.boneList["RightEyebrowUpAdjust"].boneTransform.localPosition = umaData.boneList["RightEyebrowUpAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0 + (umaDna.foreheadSize - 0.5f) * -0.015f, -0.025f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.foreheadPosition - 0.5f) * -0.007f + (umaDna.foreheadSize - 0.5f) * -0.005f, -0.010f, 0.005f));
		
		
        umaData.boneList["LipsSuperiorAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
        umaData.boneList["LipsInferiorAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 1.0f, 0.65f, 1.5f));

        umaData.boneList["LeftLipsSuperiorMiddleAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
        umaData.boneList["RightLipsSuperiorMiddleAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
        umaData.boneList["LeftLipsInferiorAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));
        umaData.boneList["RightLipsInferiorAdjust"].boneTransform.localScale = new Vector3(
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.05f, 1.0f, 1.05f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f),
        Mathf.Clamp(1 + (umaDna.lipsSize - 0.5f) * 0.9f, 0.65f, 1.5f));


        umaData.boneList["LipsInferiorAdjust"].boneTransform.localPosition = umaData.boneList["LipsInferiorAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f));

        umaData.boneList["LeftLipsAdjust"].boneTransform.localPosition = umaData.boneList["LeftLipsAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.03f, -0.02f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f));
        umaData.boneList["RightLipsAdjust"].boneTransform.localPosition = umaData.boneList["RightLipsAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.03f, -0.005f, 0.02f),
        Mathf.Clamp(0, -0.05f, 0.05f));



        umaData.boneList["LeftLipsSuperiorMiddleAdjust"].boneTransform.localPosition = umaData.boneList["LeftLipsSuperiorMiddleAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
        Mathf.Clamp(0, -0.05f, 0.05f));
        umaData.boneList["RightLipsSuperiorMiddleAdjust"].boneTransform.localPosition = umaData.boneList["RightLipsSuperiorMiddleAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
        Mathf.Clamp(0, -0.05f, 0.05f));
        umaData.boneList["LeftLipsInferiorAdjust"].boneTransform.localPosition = umaData.boneList["LeftLipsInferiorAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * 0.007f, -0.02f, 0.005f),
        Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f));
        umaData.boneList["RightLipsInferiorAdjust"].boneTransform.localPosition = umaData.boneList["RightLipsInferiorAdjust"].actualBonePosition + new Vector3(
        Mathf.Clamp(0, -0.05f, 0.05f),
        Mathf.Clamp(0 + (umaDna.mouthSize - 0.5f) * -0.007f, -0.005f, 0.02f),
        Mathf.Clamp(0 + (umaDna.lipsSize - 0.5f) * -0.008f, -0.1f, 0.1f));


        //Bone structure change
        umaData.ChangeBoneScale("Global",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
        Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
        Mathf.Clamp(1 + (umaDna.height - 0.5f) * 1.0f + (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));

        umaData.ChangeBoneMoveRelative("Position", new Vector3(
        Mathf.Clamp((umaDna.feetSize - 0.5f) * -0.27f, -0.15f, 0.0675f),
        Mathf.Clamp(0, -10, 10),
        Mathf.Clamp(0, -10, 10)));

        umaData.ChangeBoneScale("LowerBack",
        new Vector3(
        Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
        Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f),
        Mathf.Clamp(1 - (umaDna.legsSize - 0.5f) * 1.0f, 0.5f, 3.0f)));

        umaData.ChangeBoneScale("Head",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
        Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2),
        Mathf.Clamp(1 + (umaDna.headSize - 0.5f) * 2.0f, 0.5f, 2)));


        umaData.ChangeBoneScale("LeftArm",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
        umaData.ChangeBoneScale("RightArm",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.armLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
		
		umaData.ChangeBoneScale("LeftForeArm",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));
        umaData.ChangeBoneScale("RightForeArm",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.forearmLength - 0.5f) * 2.0f, 0.5f, 2.0f)));


        umaData.ChangeBoneScale("LeftHand",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
        umaData.ChangeBoneScale("RightHand",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.handsSize - 0.5f) * 2.0f, 0.5f, 2.0f)));


        umaData.ChangeBoneScale("LeftFoot",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));
        umaData.ChangeBoneScale("RightFoot",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f),
        Mathf.Clamp(1 + (umaDna.feetSize - 0.5f) * 2.0f, 0.5f, 2.0f)));		
		
        umaData.ChangeBoneMoveRelative("LeftUpLeg",new Vector3(
        Mathf.Clamp(0, -10, 10),
        Mathf.Clamp((umaDna.legSeparation - 0.5f) * -0.15f + (umaDna.lowerWeight - 0.5f) * -0.035f + (umaDna.legsSize - 0.5f) * 0.1f, -0.055f, 0.055f),
        Mathf.Clamp(0, -10, 10)));
        umaData.ChangeBoneMoveRelative("RightUpLeg", new Vector3(
        Mathf.Clamp(0, -10, 10),
        Mathf.Clamp((umaDna.legSeparation - 0.5f) * 0.15f + (umaDna.lowerWeight - 0.5f) * 0.035f + (umaDna.legsSize - 0.5f) * -0.1f, -0.025f, 0.025f),
        Mathf.Clamp(0, -10, 10)));


        umaData.ChangeBoneMoveRelative("LeftShoulder",new Vector3(
        Mathf.Clamp(0, -10, 10),
        Mathf.Clamp((umaDna.upperMuscle - 0.5f) * -0.0235f, -0.025f, 0.015f),
        Mathf.Clamp(0, -10, 10)));
        umaData.ChangeBoneMoveRelative("RightShoulder", new Vector3(
        Mathf.Clamp(0, -10, 10),
        Mathf.Clamp((umaDna.upperMuscle - 0.5f) * 0.0235f, -0.015f, 0.025f),
        Mathf.Clamp(0, -10, 10)));


        umaData.ChangeBoneScale("Mandible",
        new Vector3(
        Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
        Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f),
        Mathf.Clamp(1 + (umaDna.mandibleSize - 0.5f) * 0.35f, 0.35f, 1.35f)));
    }


}