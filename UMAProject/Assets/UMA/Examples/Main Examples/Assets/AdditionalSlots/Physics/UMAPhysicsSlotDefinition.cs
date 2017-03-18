using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA.PhysicsAvatar;

namespace UMA
{	
	public class UMAPhysicsSlotDefinition : MonoBehaviour 
	{
		[HideInInspector]
		public int ragdollLayer = 8;
		[HideInInspector]
		public int playerLayer = 9;

		[Tooltip("Set this to true if you know the player will use a capsule collider and rigidbody")]
		public bool simplePlayerCollider = true;

		public List<UMAPhysicsElement> PhysicsElements;

		public void OnSkeletonAvailable(UMAData umaData)
		{
			if (!umaData.gameObject.GetComponent<UMAPhysicsAvatar> ()) 
			{
				UMAPhysicsAvatar physicsAvatar = umaData.gameObject.AddComponent<UMAPhysicsAvatar>();
				physicsAvatar.simplePlayerCollider = simplePlayerCollider;
				physicsAvatar.ragdollLayer = ragdollLayer;
				physicsAvatar.playerLayer = playerLayer;
				physicsAvatar.CreatePhysicsObjects (PhysicsElements);
			}
		}
	}
}