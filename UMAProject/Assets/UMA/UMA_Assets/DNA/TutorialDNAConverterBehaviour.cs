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

		skeleton.SetPosition(UMASkeleton.StringToHash("LeftEye"),
							skeleton.GetPosition(UMASkeleton.StringToHash("LeftEye")) +
							new Vector3(0f, -spacing, 0f));
		skeleton.SetPosition(UMASkeleton.StringToHash("RightEye"),
							skeleton.GetPosition(UMASkeleton.StringToHash("RightEye")) +
							new Vector3(0f, spacing, 0f));

    }
}
