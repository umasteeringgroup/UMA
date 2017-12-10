using System;
using System.Collections.Generic;
using UMA;
using UnityEngine;


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
    private Transform thisParent;                                // Parent object for calculating global movement. This is currently pulled from the Anchor bone.
    private Rigidbody thisRigidbody;                             // The RigidBody applied to the Pendulum.
    private Vector3 parentPosLastFrame;                          // last frames position. Used to calculate movement (and hence force)

    // This should be attached to the last bone in the chain
    public void OnDNAApplied(UMA.UMAData dta)
    {
        umaData = dta;
        // Find Anchor Bone
        AnchorBone = SetupAnchorBone(AnchorBoneName);
        thisParent = AnchorBone;
        parentPosLastFrame = thisParent.position;

        // Setup Swing Bones
        SetupSwingBones(SwingBoneNames);
        thisRigidbody = SwingBones[SwingBones.Length-1].gameObject.GetComponent<Rigidbody>();
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
                Transform t = RecursiveFindBone(AnchorBone, s);
                SwingBones[i] = t;

                if (t== null)
                {
                    Debug.Log("Transform for Swingbone "+s+" not found");
                    continue;
                }

                GameObject go = t.gameObject;
                go.layer = 8; // our ragdoll layer
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
        Transform t = RecursiveFindBone(umaData.gameObject.transform, Name);// FindParentBone(Name);
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

    /// <summary>
    /// Looks up the hierarchy for a bone with the passed name
    /// </summary>
    /// <param name="Name"></param>
    /// <returns></returns>
    private Transform FindParentBone(string Name)
    {
        Transform Bone = gameObject.transform;

        while (Bone != null)
        {
            if (String.Compare(Bone.name,Name,true) == 0)
            {
                return Bone;
            }
            Bone = Bone.parent;
        }
        return null;
    }

    /// <summary>
    /// Find a bone by name, given a bone somewhere above the hierarchy
    /// </summary>
    /// <param name="bone"></param>
    /// <param name="raceRoot"></param>
    /// <returns></returns>
    private Transform RecursiveFindBone(Transform bone, string raceRoot)
    {
        if (String.Compare(bone.name,raceRoot,true) == 0) return bone;

        for (int i = 0; i < bone.childCount; i++)
        {
            var result = RecursiveFindBone(bone.GetChild(i), raceRoot);
            if (result != null) return result;
        }
        return null;
    }

    /*
    /// <summary>
    /// Apply force from movement
    /// </summary>
    public void Update()
    {
        if (thisParent == null) return; // wait for this to be setup before applying forces.
        if (!ApplyGlobalForces) return;

        // Calculate global movement
        Vector3 Force = (parentPosLastFrame - thisParent.position) * ForceMultiplier;
        float Magnitude = Force.magnitude;

        // small movements don't add force
        if (Magnitude > MinGlobalForce)
        {
            thisRigidbody.AddForce(Vector3.ClampMagnitude(Force, MaxGlobalForce));
        }
        parentPosLastFrame = thisParent.position;
    } */
}