using UnityEngine;
using System.Collections;
using UMA;

public class TutorialDNAConverterBehaviour : DnaConverterBehaviour
{
	public TutorialDNAConverterBehaviour()
    {
        this.ApplyDnaAction = UpdateTutorialBones;
        this.DNAType = typeof(UMADnaTutorial);
    }

	public static void UpdateTutorialBones(UMAData umaData, UMASkeleton skeleton)
    {
		var umaDna = umaData.GetDna<UMADnaTutorial>();

		float spacing = (umaDna.eyeSpacing - 0.5f) * 0.01f;

		skeleton.SetPosition(UMAUtils.StringToHash("LeftEye"),
							skeleton.GetPosition(UMAUtils.StringToHash("LeftEye")) +
							new Vector3(0f, -spacing, 0f));
		skeleton.SetPosition(UMAUtils.StringToHash("RightEye"),
							skeleton.GetPosition(UMAUtils.StringToHash("RightEye")) +
							new Vector3(0f, spacing, 0f));

    }
}
