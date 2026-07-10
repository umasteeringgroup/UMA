using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// jiggler for long bone chains such as ponytails, tails, ropes,
    /// skirts, and hanging clothing bones.
    ///
    /// This simulates world-space particles at each bone joint, keeps the root anchored
    /// to animation, constrains each segment to its original bone length, then rotates
    /// the actual bones toward the simulated child particles. Normal bone chains should
    /// use rotationWeight = 1 and positionWeight = 0 so the rig sways without changing
    /// bind-pose offsets.
    ///
    /// Can be used standalone (place on a GameObject, auto-builds in Start/LateUpdate)
    /// or driven by a UMAChainJiggleAnimator asset via Setup()/DoSimulateStep().
    /// </summary>
    [ExecuteAlways]
    public class UMAChainJiggle : MonoBehaviour
    {
        [Header("Chain Setup")]
        [Tooltip("First bone in the chain. Children are collected recursively. If null, this transform is used.")]
        public Transform rootBone;

        [Tooltip("Bones to exclude. Excluded bones and their children are skipped.")]
        public List<Transform> exclusions = new List<Transform>();

        [Tooltip("Rebuild the chain when this component starts or is enabled.")]
        public bool autoBuild = true;

        [Tooltip("Allow simulation while not in play mode. Useful for tuning, but it will move scene bones.")]
        public bool simulateInEditMode;

        [Header("Terminal Bones")]
        [Tooltip("Adds a virtual child to leaf bones so the last real bone can rotate. Set to 0 if your chain already has end bones.")]
        public float endLength = 0.15f;

        [Tooltip("Explicit local offset for virtual leaf children. When zero, endLength follows the leaf's parent-to-leaf direction.")]
        public Vector3 endOffset = Vector3.zero;

        [Header("Physics")]
        [Range(0f, 1f)]
        [Tooltip("Spring strength pulling particles back to their animated/rest pose.")]
        public float stiffness = 0.15f;

        [Range(0.001f, 5f)]
        [Tooltip("Resistance to acceleration.")]
        public float mass = 0.9f;

        [Range(0f, 1f)]
        [Tooltip("Velocity damping. Higher values settle faster.")]
        public float damping = 0.15f;

        [Range(0f, 2f)]
        [Tooltip("Downward world-space acceleration.")]
        public float gravity = 0.1f;

        [Range(0f, 1f)]
        [Tooltip("How much free links resist being carried by root movement. Higher values create more swing lag.")]
        public float inertia = 0.65f;

        [Tooltip("Base world-space distance each particle can move from its rest target. The allowed range grows down the chain.")]
        public float maxDistance = 0.35f;

        [Range(1, 8)]
        [Tooltip("Number of length-constraint passes. Longer chains usually need 2-4.")]
        public int constraintIterations = 3;

        [Header("Bone Output")]
        [Range(0f, 1f)]
        [Tooltip("How much bones rotate toward simulated child particles. Use 1 for normal ponytail/clothing chains.")]
        public float rotationWeight = 1f;

        [Range(0f, 1f)]
        [Tooltip("Optional direct joint translation. Use 0 for normal skinned bone chains to preserve bind offsets.")]
        public float positionWeight = 0f;

        [Header("Freeze Axes")]
        [Tooltip("Freeze movement on X axis in world space.")]
        public bool freezeX;

        [Tooltip("Freeze movement on Y axis in world space.")]
        public bool freezeY;

        [Tooltip("Freeze movement on Z axis in world space.")]
        public bool freezeZ;

        private readonly List<ChainParticle> _particles = new List<ChainParticle>();
        private readonly List<int> _realParticleIndices = new List<int>();
        private int _maxDepth;
        private bool _built;
        private bool _drivenExternally;

        private const float Epsilon = 0.000001f;

        private class ChainParticle
        {
            public Transform transform;
            public int parentIndex;
            public int depth;
            public readonly List<int> childIndices = new List<int>();
            public bool isVirtualEnd;
            public Vector3 restLocalPosition;
            public Quaternion restLocalRotation;
            public Vector3 restLocalScale;
            public float restLength;
            public Vector3 targetPosition;
            public Quaternion targetRotation;
            public Vector3 dynamicPosition;
            public Vector3 previousTargetPosition;
            public Vector3 velocity;
            public bool initialized;
        }

        void Start()
        {
            if (autoBuild)
            {
                BuildChain();
            }
        }

        void OnEnable()
        {
            if (autoBuild)
            {
                BuildChain();
            }
        }

        void LateUpdate()
        {
            if (_drivenExternally)
            {
                return;
            }

            if (!Application.isPlaying && !simulateInEditMode)
            {
                return;
            }

            if (!_built)
            {
                BuildChain();
            }

            if (_particles.Count == 0)
            {
                return;
            }

            DoSimulateStep(Time.deltaTime);
        }

        /// <summary>
        /// Configure the chain for animator-driven use. Call once from a UMA bone animator.
        /// </summary>
        public void Setup(
            Transform chainRoot,
            float stiffnessValue,
            float massValue,
            float dampingValue,
            float gravityValue,
            float inertiaValue,
            float maxDistanceValue,
            int constraintIterationsValue,
            float rotationWeightValue,
            float positionWeightValue,
            float endLengthValue,
            Vector3 endOffsetValue,
            bool freezeXValue,
            bool freezeYValue,
            bool freezeZValue,
            IList<Transform> exclusionTransforms = null)
        {
            _drivenExternally = true;
            rootBone = chainRoot;
            exclusions.Clear();
            if (exclusionTransforms != null)
            {
                for (int i = 0; i < exclusionTransforms.Count; i++)
                {
                    Transform exclusion = exclusionTransforms[i];
                    if (exclusion != null)
                    {
                        exclusions.Add(exclusion);
                    }
                }
            }
            stiffness = stiffnessValue;
            mass = massValue;
            damping = dampingValue;
            gravity = gravityValue;
            inertia = inertiaValue;
            maxDistance = maxDistanceValue;
            constraintIterations = constraintIterationsValue;
            rotationWeight = rotationWeightValue;
            positionWeight = positionWeightValue;
            endLength = endLengthValue;
            endOffset = endOffsetValue;
            freezeX = freezeXValue;
            freezeY = freezeYValue;
            freezeZ = freezeZValue;
            autoBuild = false;
            simulateInEditMode = false;
            BuildChain();

            if (BoneCount <= 1)
            {
                string rootName = chainRoot != null ? chainRoot.name : "<null>";
                Debug.LogWarning($"[UMAChainJiggle] Chain root '{rootName}' built only {BoneCount} real bone(s). Choose the first bone in the chain, not the leaf/tip bone, and verify the generated skeleton hierarchy contains child bones under this anchor.", this);
            }
        }

        /// <summary>
        /// Run one simulation step with the given delta time.
        /// Call from a UMA bone animator's DoUpdate.
        /// </summary>
        public void DoSimulateStep(float deltaTime)
        {
            if (!_built)
            {
                BuildChain();
            }

            if (_particles.Count == 0)
            {
                return;
            }

            SimulateChainWithDelta(deltaTime);
        }

        public int ParticleCount => _particles.Count;

        public int BoneCount => _realParticleIndices.Count;

        public Transform GetBone(int index)
        {
            if (index < 0 || index >= _realParticleIndices.Count)
            {
                return null;
            }

            return _particles[_realParticleIndices[index]].transform;
        }

        public void BuildChain()
        {
            _particles.Clear();
            _realParticleIndices.Clear();
            _maxDepth = 0;

            Transform chainRoot = rootBone != null ? rootBone : transform;
            if (chainRoot == null)
            {
                _built = false;
                return;
            }

            CollectParticles(chainRoot, -1);
            AddVirtualEndParticles();
            _built = _particles.Count > 0;
            ResetSimulation();
        }

        public void ResetSimulation()
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                ChainParticle particle = _particles[i];
                particle.initialized = false;
                particle.velocity = Vector3.zero;
            }
        }

        private void CollectParticles(Transform bone, int parentIndex)
        {
            if (bone == null || exclusions.Contains(bone))
            {
                return;
            }

            ChainParticle particle = new ChainParticle
            {
                transform = bone,
                parentIndex = parentIndex,
                depth = parentIndex >= 0 ? _particles[parentIndex].depth + 1 : 0,
                restLocalPosition = bone.localPosition,
                restLocalRotation = bone.localRotation,
                restLocalScale = bone.localScale,
                restLength = parentIndex >= 0 ? Vector3.Distance(_particles[parentIndex].transform.position, bone.position) : 0f,
                isVirtualEnd = false
            };

            int particleIndex = _particles.Count;
            _particles.Add(particle);
            _realParticleIndices.Add(particleIndex);
            _maxDepth = Mathf.Max(_maxDepth, particle.depth);

            if (parentIndex >= 0)
            {
                _particles[parentIndex].childIndices.Add(particleIndex);
            }

            foreach (Transform child in bone)
            {
                CollectParticles(child, particleIndex);
            }
        }

        private void AddVirtualEndParticles()
        {
            if (endLength <= 0f && endOffset.sqrMagnitude <= Epsilon)
            {
                return;
            }

            int realCount = _particles.Count;
            for (int i = 0; i < realCount; i++)
            {
                ChainParticle parent = _particles[i];
                if (parent.isVirtualEnd || parent.childIndices.Count > 0 || parent.transform == null)
                {
                    continue;
                }

                Vector3 localEndOffset = endOffset.sqrMagnitude > Epsilon ? endOffset : GuessEndOffset(parent);
                if (localEndOffset.sqrMagnitude <= Epsilon)
                {
                    continue;
                }

                ChainParticle endParticle = new ChainParticle
                {
                    transform = null,
                    parentIndex = i,
                    depth = parent.depth + 1,
                    restLocalPosition = localEndOffset,
                    restLocalRotation = Quaternion.identity,
                    restLocalScale = Vector3.one,
                    restLength = GetVirtualEndWorldOffset(parent, localEndOffset).magnitude,
                    isVirtualEnd = true
                };

                int endIndex = _particles.Count;
                _particles.Add(endParticle);
                parent.childIndices.Add(endIndex);
                _maxDepth = Mathf.Max(_maxDepth, endParticle.depth);
            }
        }

        private Vector3 GuessEndOffset(ChainParticle parent)
        {
            if (parent.transform == null)
            {
                return Vector3.zero;
            }

            if (parent.parentIndex >= 0)
            {
                ChainParticle grandParent = _particles[parent.parentIndex];
                Transform grandParentTransform = grandParent.transform;
                if (grandParentTransform != null)
                {
                    Vector3 direction = parent.transform.position - grandParentTransform.position;
                    if (direction.sqrMagnitude > Epsilon)
                    {
                        return parent.transform.InverseTransformVector(direction.normalized * Mathf.Max(endLength, 0.001f));
                    }
                }
            }

            return Vector3.forward * Mathf.Max(endLength, 0.001f);
        }

        private Vector3 GetVirtualEndWorldOffset(ChainParticle parent, Vector3 localEndOffset)
        {
            if (parent != null && parent.transform != null)
            {
                return parent.transform.TransformVector(localEndOffset);
            }

            return parent != null ? parent.targetRotation * localEndOffset : localEndOffset;
        }

        private void SimulateChain()
        {
            SimulateChainWithDelta(Time.deltaTime);
        }

        private void SimulateChainWithDelta(float deltaTime)
        {
            RestoreRestTransforms();
            UpdateTargets();
            InitializeParticlesIfNeeded();
            SimulateParticles(deltaTime);
            SatisfyLengthConstraints();
            ApplyParticlesToBones();
        }

        private void RestoreRestTransforms()
        {
            for (int i = 0; i < _realParticleIndices.Count; i++)
            {
                ChainParticle particle = _particles[_realParticleIndices[i]];
                if (particle.transform == null)
                {
                    continue;
                }

                particle.transform.localPosition = particle.restLocalPosition;
                particle.transform.localRotation = particle.restLocalRotation;
                particle.transform.localScale = particle.restLocalScale;
            }
        }

        private void UpdateTargets()
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                ChainParticle particle = _particles[i];
                if (!particle.isVirtualEnd)
                {
                    particle.targetPosition = particle.transform.position;
                    particle.targetRotation = particle.transform.rotation;
                    continue;
                }

                ChainParticle parent = _particles[particle.parentIndex];
                particle.targetPosition = parent.targetPosition + GetVirtualEndWorldOffset(parent, particle.restLocalPosition);
                particle.targetRotation = parent.targetRotation;
            }
        }

        private void InitializeParticlesIfNeeded()
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                ChainParticle particle = _particles[i];
                if (particle.initialized)
                {
                    continue;
                }

                particle.dynamicPosition = particle.targetPosition;
                particle.previousTargetPosition = particle.targetPosition;
                particle.velocity = Vector3.zero;
                particle.initialized = true;
            }
        }

        private void SimulateParticles(float deltaTime)
        {
            float simulationStep = Mathf.Clamp(deltaTime > 0f ? deltaTime * 60f : 1f, 0f, 2f);
            float dampingFactor = Mathf.Pow(Mathf.Clamp01(1f - damping), simulationStep);
            float safeMass = Mathf.Max(mass, 0.0001f);
            float clampedInertia = Mathf.Clamp01(inertia);
            float clampedMaxDistance = Mathf.Max(maxDistance, 0.001f);
            Vector3 freezeMask = new Vector3(freezeX ? 0f : 1f, freezeY ? 0f : 1f, freezeZ ? 0f : 1f);
            Vector3 rootDelta = _particles.Count > 0 ? _particles[0].targetPosition - _particles[0].previousTargetPosition : Vector3.zero;

            for (int i = 0; i < _particles.Count; i++)
            {
                ChainParticle particle = _particles[i];
                if (particle.parentIndex < 0)
                {
                    particle.dynamicPosition = particle.targetPosition;
                    particle.previousTargetPosition = particle.targetPosition;
                    particle.velocity = Vector3.zero;
                    continue;
                }

                float depthWeight = _maxDepth > 0 ? Mathf.Clamp01((float)particle.depth / _maxDepth) : 1f;
                float inertiaWeight = Mathf.Lerp(0.35f, 1f, depthWeight);
                float stiffnessWeight = Mathf.Lerp(0.65f, 0.15f, depthWeight);
                float particleMaxDistance = clampedMaxDistance * Mathf.Max(1f, particle.depth);

                if (rootDelta.sqrMagnitude > Epsilon && clampedInertia < 1f)
                {
                    Vector3 carriedMotion = rootDelta * (1f - clampedInertia) * Mathf.Lerp(1f, 0.35f, depthWeight);
                    particle.dynamicPosition += carriedMotion;
                }

                Vector3 force = (particle.targetPosition - particle.dynamicPosition) * stiffness * stiffnessWeight;
                force += Vector3.down * (gravity / 10f) * inertiaWeight;
                Vector3 acceleration = force / safeMass;

                particle.velocity += acceleration * simulationStep;
                particle.velocity *= dampingFactor;
                particle.dynamicPosition += particle.velocity * simulationStep;

                ApplyMotionLimits(particle, freezeMask, particleMaxDistance);

                particle.previousTargetPosition = particle.targetPosition;
            }
        }

        private void ApplyMotionLimits(ChainParticle particle, Vector3 freezeMask, float particleMaxDistance)
        {
            Vector3 offset = particle.dynamicPosition - particle.targetPosition;
            offset = Vector3.Scale(offset, freezeMask);
            if (offset.sqrMagnitude > particleMaxDistance * particleMaxDistance)
            {
                Vector3 clampedOffset = offset.normalized * particleMaxDistance;
                particle.dynamicPosition = particle.targetPosition + clampedOffset;
                particle.velocity = Vector3.ProjectOnPlane(particle.velocity, clampedOffset.normalized);
            }
            else
            {
                particle.dynamicPosition = particle.targetPosition + offset;
            }
        }

        private void SatisfyLengthConstraints()
        {
            int iterations = Mathf.Clamp(constraintIterations, 1, 8);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                for (int i = 1; i < _particles.Count; i++)
                {
                    ChainParticle particle = _particles[i];
                    ChainParticle parent = _particles[particle.parentIndex];
                    Vector3 direction = particle.dynamicPosition - parent.dynamicPosition;
                    float distance = direction.magnitude;
                    if (distance <= Epsilon || particle.restLength <= Epsilon)
                    {
                        continue;
                    }

                    Vector3 correction = direction * ((distance - particle.restLength) / distance);
                    float parentInverseMass = parent.parentIndex < 0 ? 0f : 1f;
                    const float particleInverseMass = 1f;
                    float inverseMassSum = parentInverseMass + particleInverseMass;
                    if (inverseMassSum <= Epsilon)
                    {
                        continue;
                    }

                    parent.dynamicPosition += correction * (parentInverseMass / inverseMassSum);
                    particle.dynamicPosition -= correction * (particleInverseMass / inverseMassSum);
                }
            }
        }

        private void ApplyParticlesToBones()
        {
            float clampedRotationWeight = Mathf.Clamp01(rotationWeight);
            float clampedPositionWeight = Mathf.Clamp01(positionWeight);

            for (int i = 0; i < _realParticleIndices.Count; i++)
            {
                int particleIndex = _realParticleIndices[i];
                ChainParticle particle = _particles[particleIndex];
                Transform bone = particle.transform;
                if (bone == null)
                {
                    continue;
                }

                if (clampedPositionWeight > 0f && particle.parentIndex >= 0)
                {
                    ChainParticle parent = _particles[particle.parentIndex];
                    Transform parentTransform = parent.transform != null ? parent.transform : bone.parent;
                    Vector3 targetOffset = particle.dynamicPosition - particle.targetPosition;
                    if (parentTransform != null)
                    {
                        bone.localPosition = particle.restLocalPosition + parentTransform.InverseTransformVector(targetOffset * clampedPositionWeight);
                    }
                }

                int childIndex = GetPrimaryChildIndex(particle);
                if (childIndex < 0 || clampedRotationWeight <= 0f)
                {
                    bone.rotation = particle.targetRotation;
                    continue;
                }

                ChainParticle child = _particles[childIndex];
                Vector3 restDirection = child.targetPosition - particle.targetPosition;
                Vector3 dynamicDirection = child.dynamicPosition - particle.dynamicPosition;
                if (restDirection.sqrMagnitude <= Epsilon || dynamicDirection.sqrMagnitude <= Epsilon)
                {
                    bone.rotation = particle.targetRotation;
                    continue;
                }

                Quaternion rotationDelta = Quaternion.FromToRotation(restDirection.normalized, dynamicDirection.normalized);
                bone.rotation = Quaternion.Slerp(Quaternion.identity, rotationDelta, clampedRotationWeight) * particle.targetRotation;
            }
        }

        private int GetPrimaryChildIndex(ChainParticle particle)
        {
            if (particle.childIndices.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < particle.childIndices.Count; i++)
            {
                int childIndex = particle.childIndices[i];
                if (!_particles[childIndex].isVirtualEnd)
                {
                    return childIndex;
                }
            }

            return particle.childIndices[0];
        }
    }
}
