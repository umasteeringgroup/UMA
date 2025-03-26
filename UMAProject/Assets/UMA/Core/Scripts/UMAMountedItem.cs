using UMA.CharacterSystem;
using UMA;
using UnityEngine;
using UnityEngine.Events;

namespace UMA
{

    [ExecuteInEditMode]
    public class UMAMountedItem : MonoBehaviour
    {
        [Tooltip("The name of the bone. Case must match.")]
        public string BoneName;

        [Tooltip("Unique ID for this object. Example: 'RightHandMount")]
        public string ID;
        public Vector3 Position;
        public Quaternion Orientation;
        public string IgnoreTag = "UMAIgnore";

        [Tooltip("If true the object will scale to bone DNA")]
        public bool setScale = true;

        [Tooltip("Mount this item in startup. Useful when instantiating prefabs.")]
        public bool MountOnStart;


        private int BoneHash;
        private DynamicCharacterAvatar avatar;
        private Transform MountPoint;  // This is the mount point we create/update.
        private UMAData lastUmaData;

        // Start is called before the first frame update
        void Start()
        {
            Initialize();
            gameObject.tag = IgnoreTag;
        }

        private bool Initialize()
        {
            avatar = GetComponentInParent<DynamicCharacterAvatar>();
            if (avatar == null)
            {
                return false;
            }
            avatar.CharacterUpdated.AddListener(new UnityAction<UMAData>(CharacterUpdated));
#if UNITY_EDITOR
            if (!Application.isPlaying || MountOnStart)
            {
                MountPoint = EditorFindOrCreateMountpoint();
                SetMountTransform();
            }
#endif
            return true;
        }

        // Used when mounting manually.
        public bool MountItem()
        {
            if (avatar == null)
            {
                Initialize();
                if (avatar == null)
                {
                    return false;
                }
            }
            MountPoint = FindOrCreateMountpoint(avatar.umaData);
            SetMountTransform();
            return true;
        }

#if UNITY_EDITOR
        public Transform EditorFindOrCreateMountpoint()
        {
            Transform BoneTransform = SkeletonTools.RecursiveFindBone(avatar.gameObject.transform, BoneName);
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
            return CreateMountpoint(BoneTransform, avatar.gameObject.layer);
        }
#endif

        public void ResetMountPoint()
        {
            MountPoint = FindOrCreateMountpoint(avatar.umaData);
            SetMountTransform();
        }

        public Transform FindOrCreateMountpoint(UMAData umaData)
        {
            if (string.IsNullOrEmpty(BoneName))
            {
                return null;
            }
            if (umaData == null || umaData.skeleton == null)
            {
                return null;
            }
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
                    UpdateMountPoint(child);
                    return child;
                }
            }

            return CreateMountpoint(BoneTransform, umaData.gameObject.layer);
        }

        private void UpdateMountPoint(Transform newRoot)
        {
            newRoot.transform.localPosition = Position;
            newRoot.transform.localRotation = Orientation;
            newRoot.transform.localScale = Vector3.one;
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
            lastUmaData = umaData;
        }

        void LateUpdate()
        {
            if (avatar == null)
            {
                if (!Initialize())
                {
                    return;
                }
            }
            if (MountPoint != null)
            {
                // get the worldpos/orientation of the mounted object.
                // copy to this object.
                SetMountTransform();
            }
            else
            {
                if (lastUmaData != null)
                {
                    FindOrCreateMountpoint(lastUmaData);
                    SetMountTransform();
                }
            }
        }

        private void SetMountTransform()
        {
            // Debug.Log("Setting mount transform");
            if (MountPoint != null)
            {
                Vector3 globalScale = avatar.gameObject.transform.lossyScale;

                transform.position = MountPoint.position;
                transform.rotation = MountPoint.rotation;
                if (setScale == true)
                {
                    MountPoint.localScale = MountPoint.parent.localScale;
                    transform.localScale = MountPoint.localScale;
                }
            }
        }
    }
}