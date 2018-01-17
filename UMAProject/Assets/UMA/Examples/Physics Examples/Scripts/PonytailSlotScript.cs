using System;
using System.Collections.Generic;
using UMA;
using UnityEngine;

namespace UMA.Examples
{
	/// <summary>
	/// Slot recipe script intended for runtime set up of character joints to simulate a physics based pony tail.
	/// </summary>
	public class PonytailSlotScript : MonoBehaviour
	{
	    // Properties to setup the Swing chain
	    public  List<string> SwingBoneNames=new List<string>();      // The bones that will actually move. These are linked in a chain. The final bone has gravity applied to it (it's the actual Pendulum)
	    public string AnchorBoneName;                                // The bone that the first swing bone anchors to.
	    public float SwingMass = 1.0f;                               // Mass of the swing bone Rigid bodies
	    public float SwingDrag = 0.6f;                               // Amount of drag on swing bone Rigid bodies
	    public float SwingAngularDrag = 0.6f;                        // Amount of angular drag on swing bone Rigid bodies
	    public float SwingRadius = 0.04f;                            // Radius of the swing bones
	    public float AnchorColliderRadius = 0.09f;                   // Radius of the anchor collider
	    public float AnchorMass = 0.0f;                              // Mass of Anchor Bone
	    public bool FreezePositions = false;                         // Set constraints on the rigidbody to only allow rotations.
	    public Vector3 AnchorOffset = new Vector3(0.06f, 0f, -0.09f);// Offset of the anchor collider

	    // The following are properties for the Pendulum.
	    public float MinGlobalForce = 0.1f;                          // The smallest amoount of force applied during movement
	    public float MaxGlobalForce = 1.0f;                          // the highest amount of force applied
	    public float ForceMultiplier = 100f;                         // Movement is multiplied by this number to get the amount of force applied.
	    public bool ApplyGlobalForces = true;                        // Whether or not to apply global forces to the "Pendulum". If this is false, you will only get forces applied from 
	                                                                 // movement due to the animation, not from movement of the gameobject in the world.
	    private Transform[] SwingBones = new Transform[0];           // The swingbone transforms are cached here
	    private Transform AnchorBone;                                // the transform of the anchor bone

	    private UMA.UMAData umaData;                                 // UMAData of the owning UMA


	    // Setup the anchor, bones and the pendulum
	    public void OnCharacterUpdated(UMA.UMAData dta)
	    {
	        umaData = dta;
	        // Find Anchor Bone
	        AnchorBone = SetupAnchorBone(AnchorBoneName);

	        // Setup Swing Bones
	        SetupSwingBones(SwingBoneNames);
	    }

	    private void SetupSwingBones(List<string> swingBoneNames)
	    {
	        try
	        {
	            SoftJointLimit zeroJointLimit = new SoftJointLimit();
	            SoftJointLimit sixtyJointLimit = new SoftJointLimit();
	            sixtyJointLimit.limit = 60;

	            SwingBones = new Transform[swingBoneNames.Count];

	            // Add rigidbody, colliders, characterJoints to bone.
	            for (int i = 0; i < swingBoneNames.Count; i++)
	            {
	                string s = swingBoneNames[i];
	                Transform t = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash(s));
	                SwingBones[i] = t;

	                if (t== null)
	                {
	                    Debug.Log("Transform for Swingbone "+s+" not found");
	                    continue;
	                }

	                GameObject go = t.gameObject;
	                go.layer = 8; // our ragdoll layer

	                if (go.GetComponent<Rigidbody>() != null)
	                {
	                    continue;
	                }
	                Rigidbody r = go.AddComponent<Rigidbody>();
	                r.isKinematic = false;

	                // Only add gravity to the last link in the chain (the "Pendulum"). Helps prevent physics explosion.
	                if (i == SwingBones.Length - 1)
	                {
	                    // this is the Pendulum. Needs gravity. Need script to apply global force 
	                    r.useGravity = true;
	                    // 
	                    r.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
	                    UMAGlobalForceApplier GFA = go.AddComponent<UMAGlobalForceApplier>();
	                    GFA.ApplyGlobalForces = ApplyGlobalForces;
	                    GFA.ForceMultiplier = ForceMultiplier;
	                    GFA.MinGlobalForce = MinGlobalForce;
	                    GFA.MaxGlobalForce = MaxGlobalForce;
	                    GFA.MovementTracker = AnchorBone;
	                    GFA.AttachedRigidBody = r;
	                    GFA.parentPosLastFrame = AnchorBone.position;}
	                else
	                {
	                    r.useGravity = false;
	                }

	                r.maxAngularVelocity = 4;
	                r.maxDepenetrationVelocity = 3;
	                r.mass = SwingMass;
	                r.drag = SwingDrag;
	                r.angularDrag = SwingAngularDrag;

	                if (FreezePositions)
	                  r.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ;

	                SphereCollider sc = t.gameObject.AddComponent<SphereCollider>();
	                sc.radius = SwingRadius;

	                CharacterJoint c = t.gameObject.AddComponent<CharacterJoint>();

	                c.autoConfigureConnectedAnchor = true;
	                if (i == 0)
	                {
	                    c.connectedBody = AnchorBone.gameObject.GetComponent<Rigidbody>();
	                }
	                else
	                {
	                    c.connectedBody = SwingBones[i - 1].gameObject.GetComponent<Rigidbody>();
	                }
	                c.lowTwistLimit = zeroJointLimit;
	                c.highTwistLimit = zeroJointLimit;
	                c.swing1Limit = sixtyJointLimit;
	                c.swing2Limit = sixtyJointLimit;
	            }
	        }
	        catch(Exception ex)
	        {
	            Debug.LogException(ex);
	        }
	    }

	    private Transform SetupAnchorBone(string Name)
	    {
	        Transform t = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash(Name));
	        if (t == null)
	        {
	            Debug.Log("Cannot find anchor bone: " + Name);
	            t= umaData.gameObject.transform;
	        }

	        GameObject go = t.gameObject;
	        go.layer = 8; // our ragdoll layer

	        if (go.GetComponent<Rigidbody>() != null)
	            return t;

	        Rigidbody r = go.AddComponent<Rigidbody>();
	        r.isKinematic = true;
	        r.useGravity = false;
	        r.maxAngularVelocity = 4;
	        r.maxDepenetrationVelocity = 3;
	        r.mass = AnchorMass;
	        r.drag = SwingDrag;
	        r.angularDrag = SwingAngularDrag;
	        r.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ| RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;

	        SphereCollider sc = t.gameObject.AddComponent<SphereCollider>();
	        sc.radius = AnchorColliderRadius;
	        sc.center = AnchorOffset;

	        return t;
	    }
	}
}