using UnityEngine;
using System;
using System.Collections.Generic;
using UMA;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{

    /// <summary>
    /// UnityJointAnimator is a component that animates a chain of bones using Unity's physics system.
    /// It sets up rigidbodies, colliders, and character joints for the specified swing bones and anchor bone.
    /// </summary>
    public class UnityJointAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/UnityJointAnimator")]
        public static void CreateObject()
        {
            UMA.CustomAssetUtility.CreateAsset<UnityJointAnimator>();
        }
#endif
        [Tooltip("The name of the root bone for this animator. This is the bone at the top of the chain that will be animated by this animator.")]
        public string AnchorBoneName;
        private Transform AnchorBone;
        [Tooltip("The names of the bones that will be animated by this animator. These are linked in a chain. The final bone has gravity applied to it (it's the actual Pendulum).")]
        public List<string> SwingBoneNames = new List<string>();      // The bones that will actually move. These are linked in a chain. The final bone has gravity applied to it (it's the actual Pendulum)
        [Tooltip("The mass of the swing bones. This is applied to the Rigid bodies on the swing bones. The last bone in the chain (the Pendulum) has gravity applied to it.")]
        public float SwingMass = 1.0f;                               // Mass of the swing bone Rigid bodies
        [Tooltip("Amount of drag on swing bone Rigid bodies. This is applied to the Rigid bodies on the swing bones.")]
        public float SwingDrag = 0.6f;                               // Amount of drag on swing bone Rigid bodies
        [Tooltip("Amount of angular drag on swing bone Rigid bodies. This is applied to the Rigid bodies on the swing bones.")]
        public float SwingAngularDrag = 0.6f;                        // Amount of angular drag on swing bone Rigid bodies
        [Tooltip("The radius of the swing bones. This is applied to the Sphere colliders on the swing bones.")]
        public float SwingRadius = 0.04f;                            // Radius of the swing bones
        [Tooltip("The radius of the anchor bone collider. This is applied to the Sphere collider on the anchor bone.")]
        public float AnchorColliderRadius = 0.09f;                   // Radius of the anchor collider
        [Tooltip("The mass of the anchor bone. This is applied to the Rigid body on the anchor bone.")]
        public float AnchorMass = 0.0f;                              // Mass of Anchor Bone
        [Tooltip("Whether or not to freeze the positions of the swing bones. If true, the rigidbody constraints are set to only allow rotations. This is useful for pendulum-like behavior.")]
        public bool FreezePositions = false;                         // Set constraints on the rigidbody to only allow rotations.
        [Tooltip("The offset of the anchor collider. This is applied to the Sphere collider on the anchor bone. This is useful for adjusting the position of the anchor collider relative to the anchor bone.")]
        public Vector3 AnchorOffset = new Vector3(0.06f, 0f, -0.09f);// Offset of the anchor collider
        [Tooltip("The layer to add to the bone. This is used to set the layer of the swing bones and the anchor bone. The swing bones are set to a no-collision layer, and the anchor bone is set to a ragdoll layer.")]
        public int BoneLayer = 8;                                    // The layer to add to the bone.

        // The following are properties for the Pendulum.
        [Tooltip("The smallest amount of force applied during movement. This is used to determine the minimum force applied to the Pendulum based on movement.")]
        public float MinGlobalForce = 0.1f;                          // The smallest amoount of force applied during movement
        [Tooltip("The highest amount of force applied during movement. This is used to determine the maximum force applied to the Pendulum based on movement.")]
        public float MaxGlobalForce = 1.0f;                          // the highest amount of force applied
        [Tooltip("Movement is multiplied by this number to get the amount of force applied. This is used to scale the force applied to the Pendulum based on movement.")]
        public float ForceMultiplier = 100f;                         // Movement is multiplied by this number to get the amount of force applied.
        [Tooltip("Whether or not to apply global forces to the Pendulum. If this is false, you will only get forces applied from movement due to the animation, not from movement of the gameobject in the world.")]
        public bool ApplyGlobalForces = true;                        // Whether or not to apply global forces to the "Pendulum". If this is false, you will only get forces applied from 
                                                                     // movement due to the animation, not from movement of the gameobject in the world.
        private Transform[] SwingBones = new Transform[0];           // The swingbone transforms are cached here

        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);
            // Find Anchor Bone
            AnchorBone = SetupAnchorBone(AnchorBoneName);
            SetupSwingBones(SwingBoneNames);
            initialized = true;
        }

        private void SetupSwingBones(List<string> swingBoneNames)
        {
            try
            {
                SoftJointLimit zeroJointLimit = new SoftJointLimit();
                SoftJointLimit sixtyJointLimit = new SoftJointLimit();
                sixtyJointLimit.limit = 60;
                //				sixtyJointLimit.bounciness = 

                SwingBones = new Transform[swingBoneNames.Count];

                // Add rigidbody, colliders, characterJoints to bone.
                for (int i = 0; i < swingBoneNames.Count; i++)
                {
                    string s = swingBoneNames[i];
                    Transform t = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash(s));
                    SwingBones[i] = t;

                    if (t == null)
                    {
                        Debug.Log("Transform for Swingbone " + s + " not found");
                        continue;
                    }

                    GameObject go = t.gameObject;
                    go.layer = 10; // our NoCollision layer

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
                        GFA.parentPosLastFrame = AnchorBone.position;
                    }
                    else
                    {
                        r.useGravity = false;
                    }

                    r.maxAngularVelocity = 4;
                    r.maxDepenetrationVelocity = 3;
                    r.mass = SwingMass;
#if UNITY_6000_0_OR_NEWER
                    r.linearDamping = SwingDrag; // Why rename these?
                    r.angularDamping = SwingAngularDrag;
#else
	                r.drag = SwingDrag;
	                r.angularDrag = SwingAngularDrag;
#endif
                    if (FreezePositions)
                    {
                        r.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ;
                    }

                    SphereCollider sc = t.gameObject.AddComponent<SphereCollider>();
                    sc.radius = SwingRadius;
                    sc.gameObject.layer = BoneLayer;

                    CharacterJoint c = t.gameObject.AddComponent<CharacterJoint>();

                    c.enableCollision = false;
                    c.enableProjection = true;

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
            catch (Exception ex)
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
                t = umaData.gameObject.transform;
            }

            GameObject go = t.gameObject;
            go.layer = 8; // our ragdoll layer

            if (go.GetComponent<Rigidbody>() != null)
            {
                return t;
            }

            Rigidbody r = go.AddComponent<Rigidbody>();
            r.isKinematic = true;
            r.useGravity = false;
            r.maxAngularVelocity = 4;
            r.maxDepenetrationVelocity = 3;
            r.mass = AnchorMass;
#if UNITY_6000_0_OR_NEWER
            r.linearDamping = SwingDrag;
            r.angularDamping = SwingAngularDrag;
#else
	        r.drag = SwingDrag;
	        r.angularDrag = SwingAngularDrag;
#endif
            r.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;

            SphereCollider sc = t.gameObject.AddComponent<SphereCollider>();
            sc.radius = AnchorColliderRadius;
            sc.center = AnchorOffset;
            Debug.Log($"Anchor bone {Name} setup with collider radius {AnchorColliderRadius} and offset {AnchorOffset}");
            return t;
        }
    }
}