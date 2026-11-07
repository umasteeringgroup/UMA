using System.Collections.Generic;
using UnityEngine;
using UMA.Dynamics;

namespace UMA
{
    public class UMAPhysicsSlotDefinition : MonoBehaviour 
	{
		//See UMAPhysicsSlotDefinitionEditor for how these are displayed
		[HideInInspector]
		public int ragdollLayer = 8;
		[HideInInspector]
		public int playerLayer = 9;

		[Tooltip("Set this to true if you know the player will use a capsule collider and rigidbody")]
		public bool simplePlayerCollider = true;

		[Tooltip("Set this to have your body collider act as triggers when not ragdolled")]
		public bool enableColliderTriggers = false;

		[Tooltip("Set this to snap the Avatar to the position of it's hip after ragdoll is finished")]
		public bool UpdateTransformAfterRagdoll = true;

		[Tooltip("Freeze the generated ragdoll once all bodies have settled. Call WakeRagdoll before applying new forces.")]
		public bool freezeWhenRagdollRests = true;
		[Min(0.1f), Tooltip("Seconds all bodies must remain within the rest tolerances.")]
		public float ragdollRestTime = 1f;
		[Min(0f), Tooltip("Allowed world-space movement in meters over the entire rest window.")]
		public float ragdollRestPositionThreshold = 0.03f;
		[Range(0f, 180f), Tooltip("Allowed rotation in degrees over the entire rest window.")]
		public float ragdollRestRotationThreshold = 5f;

		[Tooltip("List of Physics Elements, see UMAPhysicsElement class")]
		public List<UMAPhysicsElement> PhysicsElements;

		public void OnSkeletonAvailable(UMAData umaData)
		{
			if (!umaData.gameObject.GetComponent<UMAPhysicsAvatar> ()) 
			{
				UMAPhysicsAvatar physicsAvatar = umaData.gameObject.AddComponent<UMAPhysicsAvatar>();
				physicsAvatar.simplePlayerCollider = simplePlayerCollider;
				physicsAvatar.enableColliderTriggers = enableColliderTriggers;
				physicsAvatar.UpdateTransformAfterRagdoll = UpdateTransformAfterRagdoll;
				physicsAvatar.freezeWhenRagdollRests = freezeWhenRagdollRests;
				physicsAvatar.ragdollRestTime = ragdollRestTime;
				physicsAvatar.ragdollRestPositionThreshold = ragdollRestPositionThreshold;
				physicsAvatar.ragdollRestRotationThreshold = ragdollRestRotationThreshold;
				physicsAvatar.ragdollLayer = ragdollLayer;
				physicsAvatar.playerLayer = playerLayer;
                physicsAvatar.elements = PhysicsElements;
				physicsAvatar.CreatePhysicsObjects ();
			}
		}
	}
}
