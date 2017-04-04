using UnityEngine;

namespace UMA.Dynamics
{
	[System.Serializable]
	public class ColliderDefinition
	{
		[System.Serializable]
		public enum ColliderType {Box, Sphere, Capsule}
		public ColliderType colliderType;
		public Vector3 colliderCentre;

		//Box Collider Only
		[Tooltip("The size of the box collider")]
		public Vector3 boxDimensions;

		//Sphere Collider Only
		public float sphereRadius;

		//Capsule Collider Only
		public float capsuleRadius;
		public float capsuleHeight;
		public enum Direction {X,Y,Z}
		public Direction capsuleAlignment;
	}
}