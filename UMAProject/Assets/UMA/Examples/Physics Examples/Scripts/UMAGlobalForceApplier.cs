using UnityEngine;

namespace UMA.Examples
{
    public class UMAGlobalForceApplier : MonoBehaviour
	{
	    // The following are properties for the Swinger.
	    public float MinGlobalForce = 0.1f;                          // The smallest amoount of force applied during movement
	    public float MaxGlobalForce = 1.0f;                          // the highest amount of force applied
	    public float ForceMultiplier = 100f;                         // Movement is multiplied by this number to get the amount of force applied.
	    public bool ApplyGlobalForces = true;                        // Whether or not to apply global forces to the "Swinger". If this is false, you will only get forces applied from 

	    public Transform MovementTracker;                                 // Parent object for calculating global movement. This is currently pulled from the Anchor bone.
	    public Rigidbody AttachedRigidBody;                              // The RigidBody applied to the Swinger.
	    public Vector3 parentPosLastFrame;                          // last frames position. Used to calculate movement (and hence force)


	    /// <summary>
	    /// Apply force from movement
	    /// </summary>
	    public void Update()
	    {
	        if (MovementTracker == null)
            {
                return; // wait for this to be setup before applying forces.
            }

            if (!ApplyGlobalForces)
            {
                return;
            }

            // Calculate global movement
            Vector3 Force = (parentPosLastFrame - MovementTracker.position) * ForceMultiplier;
	        float Magnitude = Force.magnitude;

	        // small movements don't add force
	        if (Magnitude > MinGlobalForce)
	        {
	            AttachedRigidBody.AddForce(Vector3.ClampMagnitude(Force, MaxGlobalForce));
	        }
	        parentPosLastFrame = MovementTracker.position;
	    }
	}
}
