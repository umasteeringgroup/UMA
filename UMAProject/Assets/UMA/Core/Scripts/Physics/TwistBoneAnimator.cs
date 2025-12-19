/// TwistBoneAnimator - UMA component for setting up twist bone relationships on characters.
/// It defines which bones should twist in response to another bone's rotation. 
/// Common use case: forearm bones that twist when the hand rotates
/// 
/// HOW IT WORKS:
/// - Create as a ScriptableObject asset via Assets > Create > UMA > Physics > TwistBoneAnimator
/// - Configure a "driver" bone (e.g., l_hand) and specify which axis to track (X, Y, or Z)
/// - Add "twist" bones that should rotate based on the driver (e.g., forearm twist bones)
/// - Set each twist bone's ratio (0-1) to control how much it inherits the driver's rotation
/// - Add to your UMA slot's "Animated Bones" list
/// - On character creation, it will auto-register with the TwistBoneManager (attached to the GLIB Generator)

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public class TwistBoneAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/TwistBoneAnimator")]
        public static void CreateAsset()
        {
            UMA.CustomAssetUtility.CreateAsset<TwistBoneAnimator>();
        }
#endif

        [System.Serializable]
        public class TwistBoneSetup
        {
            [Tooltip("Name of the bone that will be driven by the driver bone")]
            public string boneName;
            
            [Range(0f, 1f)]
            [Tooltip("How much of the driver's rotation this bone inherits (0-1)")]
            public float twistRatio = 0.5f;
            
            [HideInInspector]
            public Transform boneTransform;
        }

        public enum TwistAxis
        {
            X,
            Y,
            Z
        }

        [Header("Driver Bone Configuration")]
        [SerializeField]
        [Tooltip("Name of the bone that drives the twist (e.g., 'l_hand')")]
        private string driverBoneName = "";
        
        [SerializeField]
        [Tooltip("Which axis of the driver bone's rotation to track")]
        private TwistAxis driverAxis = TwistAxis.X;

        [Header("Twist Bones Configuration")]
        [SerializeField]
        [Tooltip("List of bones that will be twisted based on the driver bone")]
        private List<TwistBoneSetup> twistBones = new List<TwistBoneSetup>();

        [Tooltip("Enable debug logging")]
        public bool debugMode = false;

        private Transform driverBoneTransform;
        private TwistBoneManager twistManager;
        private int registrationId = -1;


        public override void Initialize(UMAData umaData, SlotData sd)
        {
            base.Initialize(umaData, sd);

            if (string.IsNullOrEmpty(driverBoneName))
            {
                if (debugMode)
                {
                    Debug.LogWarning($"TwistBoneAnimator: Driver bone name is not set on {umaData.gameObject.name}");
                }
                return;
            }

            if (twistBones == null || twistBones.Count == 0)
            {
                if (debugMode)
                {
                    Debug.LogWarning($"TwistBoneAnimator: No twist bones configured on {umaData.gameObject.name}");
                }
                return;
            }

            if (umaData.skeleton == null)
            {
                Debug.LogError($"TwistBoneAnimator: Skeleton is null on {umaData.gameObject.name}");
                return;
            }
            
            GameObject generatorGO = UMAAssetIndexer.Instance.Generator.gameObject;
            if (generatorGO == null)
            {
                Debug.LogError("TwistBoneAnimator: Could not find GLIB");
                return;
            }

            twistManager = generatorGO.GetComponent<TwistBoneManager>();
            if (twistManager == null)
            {
                twistManager = generatorGO.AddComponent<TwistBoneManager>();
                if (debugMode)
                {
                    Debug.Log("TwistBoneAnimator: Created TwistBoneManager on Generator GameObject");
                }
            }

            driverBoneTransform = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash(driverBoneName));
            if (driverBoneTransform == null)
            {
                Debug.LogError($"TwistBoneAnimator: Could not find driver bone '{driverBoneName}' on {umaData.gameObject.name}");
                return;
            }

            List<TwistBoneSetup> validTwistBones = new List<TwistBoneSetup>();
            HashSet<string> processedBones = new HashSet<string>();
            
            foreach (var twistBone in twistBones)
            {
                if (string.IsNullOrEmpty(twistBone.boneName))
                {
                    if (debugMode)
                    {
                        Debug.LogWarning($"TwistBoneAnimator: Empty bone name in twist bones list on {umaData.gameObject.name}");
                    }
                    continue;
                }

                if (processedBones.Contains(twistBone.boneName))
                {
                    if (debugMode)
                    {
                        Debug.LogWarning($"TwistBoneAnimator: Duplicate twist bone '{twistBone.boneName}' ignored on {umaData.gameObject.name}");
                    }
                    continue;
                }
                processedBones.Add(twistBone.boneName);

                var boneTransform = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash(twistBone.boneName));
                if (boneTransform == null)
                {
                    if (debugMode)
                    {
                        Debug.LogWarning($"TwistBoneAnimator: Could not find twist bone '{twistBone.boneName}' on {umaData.gameObject.name}");
                    }
                    continue;
                }

                twistBone.twistRatio = Mathf.Clamp01(twistBone.twistRatio);
                twistBone.boneTransform = boneTransform;
                validTwistBones.Add(twistBone);
            }

            if (validTwistBones.Count == 0)
            {
                Debug.LogError($"TwistBoneAnimator: No valid twist bones found on {umaData.gameObject.name}");
                return;
            }

            var registrationData = new TwistBoneManager.TwistGroupRegistration
            {
                characterGameObject = umaData.gameObject,
                driverBone = driverBoneTransform,
                driverAxis = GetAxisVector(driverAxis),
                twistBones = new Transform[validTwistBones.Count],
                twistRatios = new float[validTwistBones.Count],
            };

            for (int i = 0; i < validTwistBones.Count; i++)
            {
                registrationData.twistBones[i] = validTwistBones[i].boneTransform;
                registrationData.twistRatios[i] = validTwistBones[i].twistRatio;
            }

            if (!twistManager.enabled)
            {
                if (debugMode)
                {
                    Debug.LogWarning($"TwistBoneAnimator: TwistBoneManager is disabled, skipping registration for {umaData.gameObject.name}");
                }
                return;
            }

            registrationId = twistManager.RegisterTwistGroup(registrationData);
            
            if (registrationId >= 0)
            {
                initialized = true;
                if (debugMode)
                {
                    Debug.Log($"TwistBoneAnimator: Successfully registered twist group {registrationId} for {umaData.gameObject.name} " +
                             $"with {validTwistBones.Count} twist bones");
                }
            }
            else
            {
                if (debugMode)
                {
                    Debug.LogWarning($"TwistBoneAnimator: Could not register twist group for {umaData.gameObject.name} - manager may be shutting down or at capacity");
                }
            }
        }


        void OnDestroy()
        {
            UnregisterFromManager();
        }


        private void UnregisterFromManager()
        {
            if (twistManager != null && registrationId >= 0)
            {
                if (twistManager != null && twistManager.gameObject != null)
                {
                    twistManager.UnregisterTwistGroup(registrationId);
                    if (debugMode)
                    {
                        Debug.Log($"TwistBoneAnimator: Unregistered twist group {registrationId}");
                    }
                }
                
                registrationId = -1;
            }
            
            driverBoneTransform = null;
            twistManager = null;
            
            if (twistBones != null)
            {
                foreach (var twistBone in twistBones)
                {
                    if (twistBone != null)
                    {
                        twistBone.boneTransform = null;
                    }
                }
            }
            
            initialized = false;
        }


        public int GetRegistrationId()
        {
            return registrationId;
        }


        public bool IsRegistered()
        {
            return registrationId >= 0 && twistManager != null && twistManager.IsRegistrationActive(registrationId);
        }

        private Vector3 GetAxisVector(TwistAxis axis)
        {
            switch (axis)
            {
                case TwistAxis.X:
                    return Vector3.right;
                case TwistAxis.Y:
                    return Vector3.up;
                case TwistAxis.Z:
                    return Vector3.forward;
                default:
                    return Vector3.right;
            }
        }

        public bool ValidateConfiguration()
        {
            if (string.IsNullOrEmpty(driverBoneName))
            {
                Debug.LogError("TwistBoneAnimator: Driver bone name is required");
                return false;
            }

            if (twistBones == null || twistBones.Count == 0)
            {
                Debug.LogError("TwistBoneAnimator: At least one twist bone must be configured");
                return false;
            }

            HashSet<string> uniqueBones = new HashSet<string>();
            foreach (var twistBone in twistBones)
            {
                if (string.IsNullOrEmpty(twistBone.boneName))
                {
                    Debug.LogError("TwistBoneAnimator: Twist bone name cannot be empty");
                    return false;
                }

                if (!uniqueBones.Add(twistBone.boneName))
                {
                    Debug.LogError($"TwistBoneAnimator: Duplicate twist bone found: {twistBone.boneName}");
                    return false;
                }

                if (twistBone.boneName == driverBoneName)
                {
                    Debug.LogError("TwistBoneAnimator: Twist bone cannot be the same as driver bone");
                    return false;
                }
            }

            return true;
        }
    }
}
