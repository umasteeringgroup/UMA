using UnityEngine;
namespace UMA
{
	public class SwayBone : MonoBehaviour
	{
		[Range(0.0f, 1.0f)]
		[Tooltip("How much inertia each bone has - makes it more bouncy")]
		public float inertia = 0.75f;  // how much the force slows each second.

		[Range(1.0f, 2.0f)]
		[Tooltip("How far something can stretch - 1.0 = no stretch")]
		public float limit = 2.0f;

		[Range(1.0f, 4.0f)]
		[Tooltip("How much it can pull away during movement")]
		public float elasticity = 2.0f;
		[Tooltip("Only rotate. Not supported in v1")]
		public bool OrientOnly;
		[Tooltip("Also reorient bones")]
		public bool Reorient;

		protected Vector3 LastWorldPos;     // Where this was last in the world. Used for detecting world movement.
		protected Vector3 localRestingPos;  // Where this is at rest.
		protected Vector3 currentForce;
		protected Vector3 localTarget;
		Vector3 targetvector;
		protected Quaternion localOrientation; // current angles
		protected float MaxDistance;
		public float frameInertia;
		public bool isTopLevel;

		public Vector3 ViewLocalOrientation;
		public Vector3 ViewInverseLocalOrientation;
		public Vector3 ViewLocalRotation;
		public Vector3 ViewInverseLocalRotation;
		public Vector3 ViewRotation;
		public Vector3 ViewInverseRotation;

		public void Initialize()
		{
			currentForce = Vector3.zero;
			localRestingPos = transform.localPosition;
			localOrientation = transform.localRotation;
			localTarget = localRestingPos * -1;
			LastWorldPos = transform.position;
			MaxDistance = limit * localRestingPos.magnitude;
		}

		public void DoUpdate(float step)
		{
			// Get the new position.
			Vector3 worldRestingPosition = transform.parent.TransformPoint(localRestingPos);
			Vector3 worldLookAtPosition = transform.parent.position;

			Vector3 GlobalForce = (worldRestingPosition - LastWorldPos) * step * elasticity;
			if (!OrientOnly)
			{
				transform.position = LastWorldPos + GlobalForce + currentForce;
			}

			// Clamp the position at the limit.
			if ((transform.position - worldRestingPosition).magnitude > MaxDistance)
			{
				targetvector = worldRestingPosition - transform.position;
				if (!OrientOnly)
				{
					transform.position = worldRestingPosition - (targetvector.normalized * MaxDistance);
				}
			}

#if false
		// orient toward the parent. 
		if (Reorient)
		{
			//float BoneLength = Vector3.Distance(child.position, child.parent.position);
			//Scale.Set(BoneLength / 10.0f, BoneLength / 10.0f, BoneLength);
			Vector3 relativePos = transform.position - transform.parent.transform.position;
			Quaternion rotation = (relativePos == Vector3.zero) ? Quaternion.identity : Quaternion.LookRotation(relativePos);
			Quaternion LocalRotation = Quaternion.Inverse(transform.rotation) * rotation;

			ViewLocalOrientation = localOrientation.eulerAngles; // orientation without pointing.
			ViewRotation = rotation.eulerAngles;                 // orientation to point to 
			ViewLocalRotation = LocalRotation.eulerAngles;       // End orientation in local space. 

			ViewInverseLocalOrientation = Quaternion.Inverse(localOrientation).eulerAngles;
			ViewInverseRotation = Quaternion.Inverse(rotation).eulerAngles;
			ViewInverseLocalRotation = Quaternion.Inverse(LocalRotation).eulerAngles;

			transform.localRotation = LocalRotation; // rotation * localOrientation;// Quaternion.Inverse(this.localOrientation); // LocalRotation;
		}
#endif
			currentForce += GlobalForce;
			currentForce *= inertia;
			LastWorldPos = transform.position;

			targetvector = worldRestingPosition;
		}
	}
}