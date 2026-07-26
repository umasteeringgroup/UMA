using UnityEngine;
using System;
using System.Collections.Generic;
using UMA.Examples;
using UMA;

public class NewJiggler : MonoBehaviour
{
    // list of all "jiggle roots".
    // 

    // Properties to setup the Swing chain
    /*
    public List<string> SwingBoneNames = new List<string>();      // The bones that will actually move. These are linked in a chain. The final bone has gravity applied to it (it's the actual Pendulum)
    public float SwingMass = 1.0f;                               // Mass of the swing bone Rigid bodies
    public float SwingDrag = 0.6f;                               // Amount of drag on swing bone Rigid bodies
    public float SwingAngularDrag = 0.6f;                        // Amount of angular drag on swing bone Rigid bodies
    public float SwingRadius = 0.04f;                            // Radius of the swing bones
    public float AnchorColliderRadius = 0.09f;                   // Radius of the anchor collider
    public float AnchorMass = 0.0f;                              // Mass of Anchor Bone
    public bool FreezePositions = false;                         // Set constraints on the rigidbody to only allow rotations.
    public Vector3 AnchorOffset = new Vector3(0.06f, 0f, -0.09f);// Offset of the anchor collider
    public int BoneLayer = 8;                                    // The layer to add to the bone.

    // The following are properties for the Pendulum.
    public float MinGlobalForce = 0.1f;                          // The smallest amoount of force applied during movement
    public float MaxGlobalForce = 1.0f;                          // the highest amount of force applied
    public float ForceMultiplier = 100f;                         // Movement is multiplied by this number to get the amount of force applied.
    public bool ApplyGlobalForces = true;                        // Whether or not to apply global forces to the "Pendulum". If this is false, you will only get forces applied from 
                                                                 // movement due to the animation, not from movement of the gameobject in the world.
    
    private Transform[] SwingBones = new Transform[0];           // The swingbone transforms are cached here*/


    public List<SwayRootBone> RootBones = new List<SwayRootBone>(); // List of all sway root bones in the scene. These are the jiggle roots.

    public string AnchorBoneName;                                // The bone that the first swing bone anchors to.
    private Transform AnchorBone;                                // the transform of the anchor bone
    private UMA.UMAData umaData;                                 // UMAData of the owning UMA


    // Setup the anchor, bones and the pendulum
    public void OnCharacterUpdated(UMA.UMAData dta)
    {
        CollisionMatrixFixer.FixLayers();

        umaData = dta;
        // Find Anchor Bone

        AnchorBone = SetupAnchorSwayBone(AnchorBoneName);
        SetupSwayBone(AnchorBone);
    }

    private void SetupSwayBone(Transform t)
    {
        SwayRootBone SRB = t.gameObject.GetComponent<SwayRootBone>();
        if (SRB == null)
        {
            SRB = t.gameObject.AddComponent<SwayRootBone>();
        }
        SRB.elasticity = 1.1f;
        SRB.inertia = 0.75f;
        SRB.limit = 1.2f;
        SRB.Reorient = false;
        SRB.OrientOnly = false;
        SRB.enabled = true;
    }

    private Transform SetupAnchorSwayBone(string Name)
    {
        // Find the anchor bone transform
        Transform t = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash(Name));
        if (t == null)
        {
            Debug.Log("Cannot find anchor bone: " + Name);
            t = umaData.gameObject.transform;
        }
        return t;
    }
}