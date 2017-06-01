using System.Collections;
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
				physicsAvatar.ragdollLayer = ragdollLayer;
				physicsAvatar.playerLayer = playerLayer;
                physicsAvatar.elements = PhysicsElements;
				physicsAvatar.CreatePhysicsObjects ();
			}
		}
	}
}