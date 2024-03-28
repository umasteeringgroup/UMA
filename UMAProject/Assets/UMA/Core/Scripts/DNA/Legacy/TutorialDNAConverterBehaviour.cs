using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Example DNA converter behaviour. Only adjusts distance between eyes.
    /// </summary>
    public class TutorialDNAConverterBehaviour : DnaConverterBehaviour
	{
		public TutorialDNAConverterBehaviour()
	    {
	        this.ApplyDnaAction = UpdateTutorialBones;
	        this.DNAType = typeof(UMADnaTutorial);
	    }

		/// <summary>
		/// Apply the DNA information about eye spacing to a skeleton.
		/// </summary>
		/// <param name="umaData">The character data.</param>
		/// <param name="skeleton">Skeleton.</param>
		public static void UpdateTutorialBones(UMAData umaData, UMASkeleton skeleton)
	    {
			var umaDna = umaData.GetDna<UMADnaTutorial>();

			float spacing = (umaDna.eyeSpacing - 0.5f) * 0.01f;
			
			skeleton.SetPositionRelative(UMAUtils.StringToHash("LeftEye"), new Vector3(0f, -spacing, 0f));
			skeleton.SetPositionRelative(UMAUtils.StringToHash("RightEye"), new Vector3(0f, spacing, 0f));
		}
	}
}
