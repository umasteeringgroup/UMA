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

        [Range(0f, 5f)]
        [Tooltip("How much free links resist being carried by parent movement. Values >1 allow exaggerated lag.")]
        public float inertia = 0.65f;

        [Range(0f, 25f)]
        [Tooltip("Global motion multiplier. Scales inertia response and gravity. Increase for more swing (runtime tunable).")]
        public float forceMultiplier = 15f;

        [Tooltip("Base world-space distance each particle can move from its rest target. The allowed range grows slightly down the chain.")]
        public float maxDistance = 0.35f;

        [Range(1, 8)]
        [Tooltip("Number of length-constraint passes. Use 1-2 for most chains; higher stabilizes long chains but costs more.")]
        public int constraintIterations = 3;

        [Header("Smoothing")]
        [Range(0f, 1f)]
        [Tooltip("Low-pass for parent motion delta. Higher = smoother, less twitchy response to animation. 0 = no smoothing.")]
        public float targetSmoothing = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Low-pass for final bone rotation. Higher = smoother visual output.")]
        public float rotationSmoothing = 0.5f;

        [Range(0f, 20f)]
        [Tooltip("Caps velocity to prevent snapping when clamping or high inertia. 0 = disabled.")]
        public float maxVelocity = 5f;

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
            public Vector3 smoothedDelta;
            public Quaternion previousAppliedRotation;
            public bool initialized;
            public bool hasPreviousAppliedRotation;
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
            IList<Transform> exclusionTransforms = null,
            float forceMultiplierValue = 1f,
            float targetSmoothingValue = 0.35f,
            float rotationSmoothingValue = 0.5f,
            float maxVelocityValue = 5f)
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
            forceMultiplier = forceMultiplierValue;
            targetSmoothing = targetSmoothingValue;
            rotationSmoothing = rotationSmoothingValue;
            maxVelocity = maxVelocityValue;
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
                particle.hasPreviousAppliedRotation = false;
                particle.velocity = Vector3.zero;
                particle.smoothedDelta = Vector3.zero;
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

                Vector3 worldOffset = GetVirtualEndWorldOffsetByRotation(parent, localEndOffset);
                float worldLen = worldOffset.magnitude;
                if (worldLen <= Epsilon)
                {
                    worldLen = Mathf.Max(endLength, 0.001f);
                }

                ChainParticle endParticle = new ChainParticle
                {
                    transform = null,
                    parentIndex = i,
                    depth = parent.depth + 1,
                    restLocalPosition = localEndOffset,
                    restLocalRotation = Quaternion.identity,
                    restLocalScale = Vector3.one,
                    restLength = worldLen,
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
            return GetVirtualEndWorldOffsetByRotation(parent, localEndOffset);
        }

        private static Vector3 GetVirtualEndWorldOffsetByRotation(ChainParticle parent, Vector3 localEndOffset)
        {
            if (parent == null)
            {
                return localEndOffset;
            }

            if (parent.initialized)
            {
                return parent.targetRotation * localEndOffset;
            }

            if (parent.transform != null)
            {
                return parent.transform.rotation * localEndOffset;
            }

            if (parent.targetRotation != default(Quaternion))
            {
                return parent.targetRotation * localEndOffset;
            }

            return localEndOffset;
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
            EnforcePostConstraintLimits();
            ApplyParticlesToBones(deltaTime);
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
                particle.smoothedDelta = Vector3.zero;
                particle.initialized = true;
                particle.hasPreviousAppliedRotation = false;
            }
        }

        private void SimulateParticles(float deltaTime)
        {
            // Clamp delta to avoid explosions; use squared delta scale like SwayBone which is framerate independent for inertia kicks
            float rawDelta = Mathf.Clamp(deltaTime, 0f, 0.033f);
            float simulationStep = Mathf.Clamp(rawDelta * 60f, 0f, 2f);
            float safeMass = Mathf.Max(mass, 0.0001f);
            float clampedInertia = Mathf.Clamp(inertia, 0f, 5f);
            float forceMult = Mathf.Max(0f, forceMultiplier);
            float clampedMaxDistance = Mathf.Max(maxDistance, 0.001f);
            Vector3 freezeMask = new Vector3(freezeX ? 0f : 1f, freezeY ? 0f : 1f, freezeZ ? 0f : 1f);

            // Your posted preset: damping 0.99 causes near-zero velocity survival and then ProjectOnPlane snaps look like jerks.
            // Cap effective damping and remap >=0.9 to still leave motion like SwayBone does.
            float effectiveDamping = Mathf.Clamp(damping, 0f, 0.95f);
            // If stiffness is tiny (0.022 in preset) compensate to avoid drift dominating inertia
            float effectiveStiffness = Mathf.Max(stiffness, 0.01f);

            float dampingFactor = Mathf.Pow(Mathf.Clamp01(1f - effectiveDamping), simulationStep);
            float targetSmooth = Mathf.Clamp01(targetSmoothing);
            bool useDeltaSmoothing = targetSmooth > 0.001f;
            float deltaLerp = useDeltaSmoothing ? 1f - Mathf.Pow(targetSmooth, simulationStep) : 1f;
            // Inverse: 0 smoothing = immediate, 1 = very smoothed – so we invert lerp sense to keep stable
            // Actually we want smoothedDelta to approach raw delta; lerp factor for smoothing: higher targetSmoothing means slower follow
            // Use: smoothed = Lerp(smoothed, rawDelta, 1 - smoothingBlend)
            float smoothingBlend = Mathf.Clamp01(targetSmoothing);
            float rawFollow = 1f - smoothingBlend; // 0.65 default means 35% smoothing for your preset converted below

            float maxVel = maxVelocity > 0f ? maxVelocity : float.MaxValue;

            for (int i = 0; i < _particles.Count; i++)
            {
                ChainParticle particle = _particles[i];
                if (particle.parentIndex < 0)
                {
                    particle.dynamicPosition = particle.targetPosition;
                    particle.previousTargetPosition = particle.targetPosition;
                    particle.velocity = Vector3.zero;
                    particle.smoothedDelta = Vector3.zero;
                    continue;
                }

                float depthWeight = _maxDepth > 0 ? Mathf.Clamp01((float)particle.depth / _maxDepth) : 1f;
                float inertiaWeight = Mathf.Lerp(0.35f, 1f, depthWeight);
                float stiffnessWeight = Mathf.Lerp(0.65f, 0.15f, depthWeight);
                float particleMaxDistance = ComputeMaxDistance(clampedMaxDistance, depthWeight);

                Vector3 rawTargetDelta = particle.targetPosition - particle.previousTargetPosition;

                // Low-pass target delta to avoid twitch from animation jitter / head bob
                if (useDeltaSmoothing)
                {
                    // Exponential moving average
                    particle.smoothedDelta = Vector3.Lerp(particle.smoothedDelta, rawTargetDelta, rawFollow);
                }
                else
                {
                    particle.smoothedDelta = rawTargetDelta;
                }

                Vector3 deltaForInertia = particle.smoothedDelta;

                // Cap huge deltas (teleport) to avoid explosion
                if (deltaForInertia.sqrMagnitude > 1f)
                {
                    deltaForInertia = deltaForInertia.normalized * 1f;
                }

                if (deltaForInertia.sqrMagnitude > Epsilon && forceMult > 0f)
                {
                    float depthInertiaScale = Mathf.Lerp(0.35f, 1f, depthWeight);
                    // Don't multiply by simulationStep again – targetDelta already per-frame, and we already have velocity decay.
                    // This matches SwayBone's approach which keeps sway smooth regardless of framerate.
                    // old: * simulationStep caused double time scaling -> jerky when deltaTime varies
                    particle.velocity -= deltaForInertia * clampedInertia * depthInertiaScale * forceMult;
                }

                Vector3 force = (particle.targetPosition - particle.dynamicPosition) * effectiveStiffness * stiffnessWeight;
                force += Vector3.down * (gravity / 10f) * inertiaWeight * forceMult;
                Vector3 acceleration = force / safeMass;

                particle.velocity += acceleration * rawDelta;
                particle.velocity *= dampingFactor;
                particle.velocity = Vector3.Scale(particle.velocity, freezeMask);

                // Velocity clamp – main fix for jerking when clamping to maxDistance then projecting velocity
                if (maxVel < float.MaxValue && particle.velocity.sqrMagnitude > maxVel * maxVel)
                {
                    particle.velocity = particle.velocity.normalized * maxVel;
                }

                particle.dynamicPosition += particle.velocity * rawDelta * 60f * 0.016f; // normalize to ~60fps baseline

                ApplyMotionLimits(particle, freezeMask, particleMaxDistance);

                particle.previousTargetPosition = particle.targetPosition;
            }
        }

        private float ComputeMaxDistance(float baseDistance, float depthWeight)
        {
            return baseDistance * (1f + depthWeight * 0.75f);
        }

        private void ApplyMotionLimits(ChainParticle particle, Vector3 freezeMask, float particleMaxDistance)
        {
            Vector3 offset = particle.dynamicPosition - particle.targetPosition;
            offset = Vector3.Scale(offset, freezeMask);
            float sqrMax = particleMaxDistance * particleMaxDistance;
            if (offset.sqrMagnitude > sqrMax)
            {
                Vector3 clampedOffset = offset.sqrMagnitude > Epsilon ? offset.normalized * particleMaxDistance : Vector3.zero;
                particle.dynamicPosition = particle.targetPosition + clampedOffset;
                // Soften velocity projection: lerp toward plane instead of instant cut to avoid snap
                if (clampedOffset.sqrMagnitude > Epsilon)
                {
                    Vector3 projected = Vector3.ProjectOnPlane(particle.velocity, clampedOffset.normalized);
                    particle.velocity = Vector3.Lerp(particle.velocity, projected, 0.5f);
                }
            }
            else
            {
                particle.dynamicPosition = particle.targetPosition + offset;
            }
        }

        private void EnforcePostConstraintLimits()
        {
            float clampedMaxDistance = Mathf.Max(maxDistance, 0.001f);
            Vector3 freezeMask = new Vector3(freezeX ? 0f : 1f, freezeY ? 0f : 1f, freezeZ ? 0f : 1f);

            for (int i = 1; i < _particles.Count; i++)
            {
                ChainParticle particle = _particles[i];
                float depthWeight = _maxDepth > 0 ? Mathf.Clamp01((float)particle.depth / _maxDepth) : 1f;
                float particleMaxDistance = ComputeMaxDistance(clampedMaxDistance, depthWeight);
                ApplyMotionLimits(particle, freezeMask, particleMaxDistance);
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
                    if (particle.restLength <= Epsilon)
                    {
                        continue;
                    }

                    ChainParticle parent = _particles[particle.parentIndex];
                    Vector3 direction = particle.dynamicPosition - parent.dynamicPosition;
                    float distance = direction.magnitude;
                    if (distance <= Epsilon)
                    {
                        continue;
                    }

                    Vector3 desired = parent.dynamicPosition + direction * (particle.restLength / distance);
                    particle.dynamicPosition = desired;
                }
            }
        }

        private void ApplyParticlesToBones(float deltaTime)
        {
            float clampedRotationWeight = Mathf.Clamp01(rotationWeight);
            float clampedPositionWeight = Mathf.Clamp01(positionWeight);
            float rotSmooth = Mathf.Clamp01(rotationSmoothing);
            bool useRotSmooth = rotSmooth > 0.001f && Application.isPlaying;
            float rotSmoothingFactor = useRotSmooth ? 1f - Mathf.Pow(rotSmooth, Mathf.Clamp(deltaTime * 10f, 0f, 1f)) : 1f;

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
                    // store for smoothing even when no child
                    if (useRotSmooth)
                    {
                        particle.previousAppliedRotation = bone.rotation;
                        particle.hasPreviousAppliedRotation = true;
                    }
                    continue;
                }

                ChainParticle child = _particles[childIndex];
                Vector3 restDirection = child.targetPosition - particle.targetPosition;
                Vector3 dynamicDirection = child.dynamicPosition - particle.dynamicPosition;
                if (restDirection.sqrMagnitude <= Epsilon || dynamicDirection.sqrMagnitude <= Epsilon)
                {
                    bone.rotation = particle.targetRotation;
                    if (useRotSmooth)
                    {
                        particle.previousAppliedRotation = bone.rotation;
                        particle.hasPreviousAppliedRotation = true;
                    }
                    continue;
                }

                Quaternion rotationDelta = Quaternion.FromToRotation(restDirection.normalized, dynamicDirection.normalized);
                Quaternion desiredRotation = Quaternion.Slerp(Quaternion.identity, rotationDelta, clampedRotationWeight) * particle.targetRotation;

                if (useRotSmooth && particle.hasPreviousAppliedRotation)
                {
                    // Angular smoothing like SwayBone visually – prevents micro pops
                    bone.rotation = Quaternion.Slerp(particle.previousAppliedRotation, desiredRotation, rotSmoothingFactor);
                }
                else
                {
                    bone.rotation = desiredRotation;
                }

                if (useRotSmooth)
                {
                    particle.previousAppliedRotation = bone.rotation;
                    particle.hasPreviousAppliedRotation = true;
                }
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

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_particles == null || _particles.Count == 0)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 1; i < _particles.Count; i++)
            {
                ChainParticle p = _particles[i];
                ChainParticle parent = _particles[p.parentIndex];
                Gizmos.DrawLine(parent.dynamicPosition, p.dynamicPosition);
            }

            for (int i = 0; i < _particles.Count; i++)
            {
                ChainParticle p = _particles[i];
                if (p.isVirtualEnd)
                {
                    Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
                }
                else if (p.parentIndex < 0)
                {
                    Gizmos.color = Color.green;
                }
                else
                {
                    Gizmos.color = Color.yellow;
                }
                Gizmos.DrawWireSphere(p.dynamicPosition, 0.012f);
                Gizmos.DrawWireSphere(p.targetPosition, 0.008f);
            }
        }
#endif
    }
}
