/// ========================================
/// TWIST BONE MANAGER
/// ========================================
/// 
/// OVERVIEW:
/// This manager provides centralized, high-performance twist bone updates for multiple UMA characters.
/// Twist bones are secondary bones that rotate along an axis based on a driver bone's rotation,
/// commonly used for limbs (arms, legs) to create more natural deformation when joints twist.
/// 
/// HOW IT WORKS:
/// 1. Characters register their twist bone groups (driver bone + twist bones array) with the manager using TwistBoneAnimator attached to slots.
/// 2. Each frame, the manager uses Burst-compiled jobs to:
///    - Read driver bone rotations
///    - Calculate twist angles and apply ratios to twist bones
///    - Apply the calculated rotations back to twist bones
/// 3. LOD system adjusts update frequency based on camera distance for optimal performance
/// 4. All transforms are managed through TransformAccessArray for efficient job system integration
/// 
/// KEY FEATURES:
/// - Burst compilation for maximum performance
/// - Automatic LOD management based on camera distance
/// - Supports up to 500 twist joints simultaneously (configurable)
/// - Proper angle unwrapping to prevent rotation flipping
/// - Memory-efficient with NativeArrays and job system integration


using UnityEngine;
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using System.Collections.Generic;

namespace UMA
{
    public class TwistBoneManager : MonoBehaviour
    {
        [System.Serializable]
        public struct TwistGroupRegistration
        {
            public GameObject characterGameObject;
            public Transform driverBone;
            public Vector3 driverAxis;
            public Transform[] twistBones;
            public float[] twistRatios;
        }

        [Header("LOD Settings")]
        [Tooltip("Distance thresholds for LOD levels (L0, L1, L2)")]
        public float[] lodDistances = new float[] { 10f, 25f, 50f };

        [Tooltip("Update frequencies (1=every frame, 2=every 2nd, 4=every 4th, 0=disabled)")]
        public int[] lodUpdateRates = new int[] { 1, 2, 4, 0 };

        [Header("Performance")]
        [Tooltip("Maximum number of twist joints to support")]
        public int maxTwistJoints = 500;

        [Tooltip("Enable debug logging")]
        public bool debugMode = false;

        private struct RegistrationData
        {
            public GameObject characterGameObject;
            public int startJointIndex;
            public int jointCount;
        }

        [BurstCompile]
        private struct TwistJointData
        {
            public int characterIndex;
            public int driverTransformIndex;
            public int twistTransformIndex;
            public float ratio;
            public float3 axis;
        }

        private NativeArray<TwistJointData> twistJoints;
        private NativeArray<int> characterLODs;
        private NativeArray<quaternion> driverRotations;
        private NativeArray<quaternion> twistResults;
        private NativeArray<float> previousAngles;
        private NativeArray<int> lodRatesNative;
        private TransformAccessArray transforms;

        private Dictionary<int, RegistrationData> registrations = new Dictionary<int, RegistrationData>();
        private int nextRegistrationId = 0;
        private int activeJointCount = 0;

        private Camera mainCamera;
        private JobHandle currentJobHandle;

        private void Awake()
        {
            twistJoints = new NativeArray<TwistJointData>(maxTwistJoints, Allocator.Persistent);
            characterLODs = new NativeArray<int>(maxTwistJoints, Allocator.Persistent);
            driverRotations = new NativeArray<quaternion>(maxTwistJoints, Allocator.Persistent);
            twistResults = new NativeArray<quaternion>(maxTwistJoints, Allocator.Persistent);
            previousAngles = new NativeArray<float>(maxTwistJoints, Allocator.Persistent);
            lodRatesNative = new NativeArray<int>(lodUpdateRates.Length, Allocator.Persistent);
            lodRatesNative.CopyFrom(lodUpdateRates);
            
            mainCamera = Camera.main;
        }

        public int RegisterTwistGroup(TwistGroupRegistration registration)
        {
            if (!twistJoints.IsCreated || !enabled)
            {
                return -1;
            }

            if (registration.twistBones == null || registration.twistBones.Length == 0)
                return -1;

            if (registration.twistRatios == null || registration.twistRatios.Length != registration.twistBones.Length)
            {
                Debug.LogError("TwistBoneManager: twistRatios is null or length mismatch with twistBones");
                return -1;
            }

            int jointCount = registration.twistBones.Length;
            
            if (activeJointCount + jointCount > maxTwistJoints)
            {
                Debug.LogError($"TwistBoneManager: Cannot register - would exceed max joints ({maxTwistJoints})");
                return -1;
            }

            currentJobHandle.Complete();

            int registrationId = nextRegistrationId++;
            int startIndex = activeJointCount;
            int characterIndex = registrationId;

            var regData = new RegistrationData
            {
                characterGameObject = registration.characterGameObject,
                startJointIndex = startIndex,
                jointCount = jointCount
            };
            registrations[registrationId] = regData;

            var transformList = new List<Transform>(transforms.isCreated ? transforms.length : 0);
            if (transforms.isCreated)
            {
                for (int i = 0; i < transforms.length; i++)
                    transformList.Add(transforms[i]);
            }

            int driverIndex = transformList.Count;
            transformList.Add(registration.driverBone);

            for (int i = 0; i < jointCount; i++)
            {
                int twistIndex = transformList.Count;
                transformList.Add(registration.twistBones[i]);

                twistJoints[activeJointCount] = new TwistJointData
                {
                    characterIndex = characterIndex,
                    driverTransformIndex = driverIndex,
                    twistTransformIndex = twistIndex,
                    ratio = registration.twistRatios[i],
                    axis = registration.driverAxis
                };

                activeJointCount++;
            }

            if (transforms.isCreated)
                transforms.Dispose();
            
            transforms = new TransformAccessArray(transformList.ToArray());

            if (debugMode)
                Debug.Log($"Registered twist group {registrationId} with {jointCount} joints");

            return registrationId;
        }

        public void UnregisterTwistGroup(int registrationId)
        {
            if (!registrations.ContainsKey(registrationId))
                return;

            if (!twistJoints.IsCreated)
                return;

            currentJobHandle.Complete();

            var regData = registrations[registrationId];
            int startIndex = regData.startJointIndex;
            int count = regData.jointCount;

            for (int i = startIndex; i < activeJointCount - count; i++)
            {
                twistJoints[i] = twistJoints[i + count];
            }

            activeJointCount -= count;

            foreach (var kvp in registrations)
            {
                if (kvp.Value.startJointIndex > startIndex)
                {
                    var updated = kvp.Value;
                    updated.startJointIndex -= count;
                    registrations[kvp.Key] = updated;
                }
            }

            registrations.Remove(registrationId);

            RebuildTransformArray();

            if (debugMode)
                Debug.Log($"Unregistered twist group {registrationId}");
        }

        private void RebuildTransformArray()
        {
            if (!twistJoints.IsCreated)
                return;

            currentJobHandle.Complete();

            var uniqueTransforms = new HashSet<Transform>();
            var transformList = new List<Transform>();
            var indexRemap = new Dictionary<int, int>();

            for (int i = 0; i < activeJointCount; i++)
            {
                var joint = twistJoints[i];
                
                if (!indexRemap.ContainsKey(joint.driverTransformIndex))
                {
                    var driverTransform = transforms.isCreated && joint.driverTransformIndex < transforms.length 
                        ? transforms[joint.driverTransformIndex] 
                        : null;
                    
                    if (driverTransform != null && !uniqueTransforms.Contains(driverTransform))
                    {
                        indexRemap[joint.driverTransformIndex] = transformList.Count;
                        transformList.Add(driverTransform);
                        uniqueTransforms.Add(driverTransform);
                    }
                }

                if (!indexRemap.ContainsKey(joint.twistTransformIndex))
                {
                    var twistTransform = transforms.isCreated && joint.twistTransformIndex < transforms.length 
                        ? transforms[joint.twistTransformIndex] 
                        : null;
                    
                    if (twistTransform != null && !uniqueTransforms.Contains(twistTransform))
                    {
                        indexRemap[joint.twistTransformIndex] = transformList.Count;
                        transformList.Add(twistTransform);
                        uniqueTransforms.Add(twistTransform);
                    }
                }
            }

            for (int i = 0; i < activeJointCount; i++)
            {
                var joint = twistJoints[i];
                if (indexRemap.ContainsKey(joint.driverTransformIndex))
                    joint.driverTransformIndex = indexRemap[joint.driverTransformIndex];
                if (indexRemap.ContainsKey(joint.twistTransformIndex))
                    joint.twistTransformIndex = indexRemap[joint.twistTransformIndex];
                twistJoints[i] = joint;
            }

            if (transforms.isCreated)
                transforms.Dispose();

            if (transformList.Count > 0)
                transforms = new TransformAccessArray(transformList.ToArray());
        }

        private void LateUpdate()
        {
            if (!enabled || !transforms.isCreated || activeJointCount == 0)
                return;

            currentJobHandle.Complete();

            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            float3 cameraPos = mainCamera.transform.position;
            int frame = Time.frameCount;

            UpdateLODLevels(cameraPos, frame);

            var readJob = new ReadDriverRotationsJob
            {
                joints = twistJoints,
                rotations = driverRotations,
                count = activeJointCount
            }.Schedule(transforms);

            var calculateJob = new CalculateTwistJob
            {
                joints = twistJoints,
                driverRotations = driverRotations,
                results = twistResults,
                previousAngles = previousAngles,
                lods = characterLODs,
                frame = frame,
                lodRates = lodRatesNative,
                count = activeJointCount
            }.Schedule(activeJointCount, 32, readJob);

            var applyJob = new ApplyTwistJob
            {
                joints = twistJoints,
                rotations = twistResults,
                count = activeJointCount
            }.Schedule(transforms, calculateJob);

            currentJobHandle = applyJob;
        }

        private void UpdateLODLevels(float3 cameraPos, int frame)
        {
            foreach (var kvp in registrations)
            {
                var regData = kvp.Value;
                if (regData.characterGameObject == null)
                    continue;

                float distance = math.distance(cameraPos, (float3)regData.characterGameObject.transform.position);
                
                int lod = 3;
                for (int i = 0; i < lodDistances.Length; i++)
                {
                    if (distance <= lodDistances[i])
                    {
                        lod = i;
                        break;
                    }
                }

                for (int i = 0; i < regData.jointCount; i++)
                {
                    characterLODs[regData.startJointIndex + i] = lod;
                }
            }
        }

        [BurstCompile]
        private struct ReadDriverRotationsJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<TwistJointData> joints;
            [NativeDisableParallelForRestriction] public NativeArray<quaternion> rotations;
            [ReadOnly] public int count;

            public void Execute(int index, TransformAccess transform)
            {
                for (int i = 0; i < count; i++)
                {
                    if (joints[i].driverTransformIndex == index)
                    {
                        rotations[i] = transform.localRotation;
                    }
                }
            }
        }

        [BurstCompile]
        private struct CalculateTwistJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<TwistJointData> joints;
            [ReadOnly] public NativeArray<quaternion> driverRotations;
            [NativeDisableParallelForRestriction] public NativeArray<quaternion> results;
            [NativeDisableParallelForRestriction] public NativeArray<float> previousAngles;
            [ReadOnly] public NativeArray<int> lods;
            [ReadOnly] public NativeArray<int> lodRates;
            [ReadOnly] public int frame;
            [ReadOnly] public int count;

            public void Execute(int index)
            {
                if (index >= count)
                {
                    results[index] = quaternion.identity;
                    return;
                }

                var joint = joints[index];
                int lod = lods[index];
                
                if (lod >= lodRates.Length)
                {
                    results[index] = quaternion.identity;
                    return;
                }

                int updateRate = lodRates[lod];
                if (updateRate == 0 || (updateRate > 1 && (frame + joint.characterIndex) % updateRate != 0))
                {
                    results[index] = quaternion.identity;
                    return;
                }

                quaternion driverRot = driverRotations[index];
                float twistAngle = ExtractTwistAngle(driverRot, joint.axis);
                
                float previousAngle = previousAngles[index];
                float unwrappedAngle = UnwrapAngle(twistAngle, previousAngle);
                previousAngles[index] = unwrappedAngle;
                
                float finalAngle = unwrappedAngle * joint.ratio;
                quaternion result = quaternion.AxisAngle(joint.axis, finalAngle);
                results[index] = result;
            }

            private float ExtractTwistAngle(quaternion q, float3 axis)
            {
                float3 normalizedAxis = math.normalize(axis);
                float4 qv = q.value;
                
                float dot = qv.x * normalizedAxis.x + qv.y * normalizedAxis.y + qv.z * normalizedAxis.z;
                
                quaternion twist = new quaternion(
                    normalizedAxis.x * dot,
                    normalizedAxis.y * dot,
                    normalizedAxis.z * dot,
                    qv.w
                );
                
                twist = math.normalize(twist);
                
                float angle = 2.0f * math.atan2(
                    math.length(new float3(twist.value.x, twist.value.y, twist.value.z)),
                    twist.value.w
                );
                
                float3 twistAxisFromQuat = new float3(twist.value.x, twist.value.y, twist.value.z);
                if (math.lengthsq(twistAxisFromQuat) > 0.0001f)
                {
                    twistAxisFromQuat = math.normalize(twistAxisFromQuat);
                    if (math.dot(twistAxisFromQuat, normalizedAxis) < 0.0f)
                    {
                        angle = -angle;
                    }
                }
                
                return angle;
            }

            private float UnwrapAngle(float currentAngle, float previousAngle)
            {
                float delta = currentAngle - (previousAngle % (2.0f * math.PI));
                
                if (delta > math.PI)
                {
                    delta -= 2.0f * math.PI;
                }
                else if (delta < -math.PI)
                {
                    delta += 2.0f * math.PI;
                }
                
                return previousAngle + delta;
            }
        }

        [BurstCompile]
        private struct ApplyTwistJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<TwistJointData> joints;
            [ReadOnly] public NativeArray<quaternion> rotations;
            [ReadOnly] public int count;

            public void Execute(int index, TransformAccess transform)
            {
                for (int i = 0; i < count; i++)
                {
                    if (joints[i].twistTransformIndex == index)
                    {
                        var rot = rotations[i];
                        if (!rot.Equals(quaternion.identity))
                        {
                            transform.localRotation = rot;
                        }
                        break;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            currentJobHandle.Complete();
            
            if (twistJoints.IsCreated) twistJoints.Dispose();
            if (characterLODs.IsCreated) characterLODs.Dispose();
            if (driverRotations.IsCreated) driverRotations.Dispose();
            if (twistResults.IsCreated) twistResults.Dispose();
            if (previousAngles.IsCreated) previousAngles.Dispose();
            if (lodRatesNative.IsCreated) lodRatesNative.Dispose();
            if (transforms.isCreated) transforms.Dispose();
        }

        public bool IsRegistrationActive(int registrationId) => registrations.ContainsKey(registrationId);
        public int GetActiveRegistrationCount() => registrations.Count;
        public int GetActiveJointCount() => activeJointCount;
    }
}