using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA;
using UnityEngine;
using UnityEngine.Events;

[ExecuteInEditMode]
public class UMAMountedItem : MonoBehaviour
{
    [Tooltip("The name of the bone. Case must match.")]
    public string BoneName;

    [Tooltip("Unique ID for this object. Example: 'RightHandMount")]    
    public string ID;  
    public Vector3 Position;
    public Quaternion Orientation;

    private int BoneHash;
    private DynamicCharacterAvatar avatar;
    private Transform MountPoint;  // This is the mount point we create/update.

    // Start is called before the first frame update
    void Start()
    {
        avatar = GetComponentInParent<DynamicCharacterAvatar>();
        if (avatar == null)
        {
            if (Debug.isDebugBuild)
            {
                Debug.LogError("Unable to find parent for mounted item on bone: "+BoneName);
            }
            return;
        }
        avatar.CharacterUpdated.AddListener(new UnityAction<UMAData>(CharacterUpdated));
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            MountPoint = FindOrCreateMountpoint();
            SetMountTransform();
        }
#endif
    }

#if UNITY_EDITOR
    public Transform FindOrCreateMountpoint()
    {
        Debug.Log("setting mountpoint");
        Transform BoneTransform = SkeletonTools.RecursiveFindBone(avatar.gameObject.transform, BoneName);
        return CreateMountpoint(BoneTransform, avatar.gameObject.layer);
    }
#endif

    public Transform FindOrCreateMountpoint(UMAData umaData)
    {
        BoneHash = UMAUtils.StringToHash(BoneName);
        Transform BoneTransform = umaData.skeleton.GetBoneTransform(BoneHash);
        if (BoneTransform == null)
        {
            return null;
        }
        foreach (Transform child in BoneTransform)
        {
            if (child.name == ID)
            {
                return child;
            }
        }

        return CreateMountpoint(BoneTransform, umaData.gameObject.layer);
    }

    private Transform CreateMountpoint(Transform BoneTransform, int Layer)
    {
        GameObject newRoot = new GameObject(ID);
        newRoot.layer = Layer;
        newRoot.transform.parent = BoneTransform;
        newRoot.transform.localPosition = Position;
        newRoot.transform.localRotation = Orientation;
        newRoot.transform.localScale = Vector3.one;
        return newRoot.transform;
    }

    public void CharacterUpdated(UMAData umaData)
    {
        // Debug.Log("Getting bone info");
        MountPoint = FindOrCreateMountpoint(umaData);
    }

    void LateUpdate()
    {
        // get the worldpos/orientation of the mounted object.
        // copy to this object.
        SetMountTransform();
    }

    private void SetMountTransform()
    {
       // Debug.Log("Setting mount transform");
        if (MountPoint != null)
        {
         //   Debug.Log("copying");
            this.gameObject.transform.position = MountPoint.position;
            this.gameObject.transform.rotation = MountPoint.rotation;
        }
    }
}
