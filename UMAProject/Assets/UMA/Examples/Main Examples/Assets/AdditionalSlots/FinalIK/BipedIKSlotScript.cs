#if false
using UnityEngine;
using RootMotion;
using RootMotion.FinalIK;

namespace UMA.Dynamics
{
	/// <summary>
	/// Auxillary slot which adds and initializes a FinalIK BipedIK solver.
	/// </summary>
	public class BipedIKSlotScript : MonoBehaviour 
	{
		public void OnCharacterCompleted(UMAData umaData)
		{
			// Add the BipedIK component
			BipedIK bipedIK = umaData.gameObject.AddComponent<BipedIK>() as BipedIK;

			// Setup the bone references
			if (umaData.animator != null)
			{
				BipedReferences.AssignHumanoidReferences(ref bipedIK.references, umaData.animator, BipedReferences.AutoDetectParams.Default);
			}
			else
			{
				BipedReferences.AutoDetectReferences(ref bipedIK.references, umaData.umaRoot.transform, BipedReferences.AutoDetectParams.Default);
			}

			// Zero out the initial IK weights
			bipedIK.SetLookAtWeight(0f, 0f, 0f, 0f, 0f, 0f, 0f);
			bipedIK.SetSpineWeight(0f);

			bipedIK.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
			bipedIK.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
			bipedIK.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
			bipedIK.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);

			bipedIK.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
			bipedIK.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
			bipedIK.GetGoalIK(AvatarIKGoal.LeftHand).bendModifier = IKSolverLimb.BendModifier.Arm;

			bipedIK.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
			bipedIK.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
			bipedIK.GetGoalIK(AvatarIKGoal.RightHand).bendModifier = IKSolverLimb.BendModifier.Arm;
		}
	}
}
#endif