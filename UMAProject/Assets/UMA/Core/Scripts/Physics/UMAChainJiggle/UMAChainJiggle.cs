using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Tuning values for a <see cref="UMAChainJiggle"/> chain. Shared by the component
    /// and by <see cref="UMAChainJiggleAnimator"/> assets.
    ///
    /// The four motion values are defined at a 60 Hz reference rate, so a chain behaves
    /// the same at any frame rate.
    /// </summary>
    [Serializable]
    public class UMAChainJiggleSettings
    {
        public enum FreezeAxis
        {
            None,
            X,
            Y,
            Z
        }

        [Header("Motion")]
        [Range(0f, 1f)]
        [Tooltip("How quickly motion dies out. 0 keeps swinging, 1 never overshoots.")]
        public float damping = 0.2f;

        [Range(0f, 1f)]
        [Tooltip("How strongly each bone is pulled back to its authored pose. Low values are loose and slow, high values are springy and quick.")]
        public float elasticity = 0.1f;

        [Range(0f, 1f)]
        [Tooltip("How much of the authored shape is always kept. 0 lets a bone swing freely, 1 keeps it rigid. Use this to stop hair folding back on itself.")]
        public float stiffness = 0.2f;

        [Range(0f, 1f)]
        [Tooltip("How much of the character's own travel is ignored. 0 trails behind every step the character takes, 1 only reacts to rotation and acceleration of the parent bone.")]
        public float inertia = 0.5f;

        [Header("Distribution along the chain (0 = root, 1 = tip)")]
        [Tooltip("Multiplier for Damping along the chain. Leave empty for the same value everywhere.")]
        public AnimationCurve dampingDistribution = new AnimationCurve();

        [Tooltip("Multiplier for Elasticity along the chain. Leave empty for the same value everywhere.")]
        public AnimationCurve elasticityDistribution = new AnimationCurve();

        [Tooltip("Multiplier for Stiffness along the chain. Leave empty for the same value everywhere.")]
        public AnimationCurve stiffnessDistribution = new AnimationCurve();

        [Tooltip("Multiplier for Inertia along the chain. Leave empty for the same value everywhere.")]
        public AnimationCurve inertiaDistribution = new AnimationCurve();

        [Header("Forces")]
        [Range(0f, 20f)]
        [Tooltip("Downward world-space acceleration in metres per second squared. 9.81 is real gravity.")]
        public float gravity = 9.81f;

        [Tooltip("The chain was modelled already hanging under gravity (true for almost all hair and anatomy). The authored pose then stays put while the character is upright, and gravity only shows when the parent bone tilts.")]
        public bool restPoseIncludesGravity = true;

        [Tooltip("Constant world-space acceleration in metres per second squared, for wind or similar.")]
        public Vector3 force = Vector3.zero;

        [Header("Limits")]
        [Tooltip("Stops bones moving along this axis of their parent bone.")]
        public FreezeAxis freezeAxis = FreezeAxis.None;

        [Min(0f)]
        [Tooltip("Collision radius of each joint in metres. Only used when colliders are assigned.")]
        public float radius = 0.02f;

        [Header("Terminal Bones")]
        [Min(0f)]
        [Tooltip("Length in metres of a virtual tip added to each leaf bone so the last real bone can rotate. Leaf bones with nothing weighted to them (end bones) are harmless either way.")]
        public float endLength = 0.15f;

        [Tooltip("Explicit local offset for the virtual tip. When zero, the tip continues in the direction of the leaf bone.")]
        public Vector3 endOffset = Vector3.zero;

        [Header("Output")]
        [Range(0f, 1f)]
        [Tooltip("Blend between the authored pose (0) and the simulated pose (1).")]
        public float blendWeight = 1f;

        [Header("Simulation")]
        [Range(60, 240)]
        [Tooltip("Lowest simulation rate. Frames slower than this are split into sub-steps.")]
        public int updateRate = 60;

        [Range(1, 8)]
        [Tooltip("Most sub-steps run in one frame. Below updateRate / maxSubsteps frames per second the chain moves in slow motion instead of costing more.")]
        public int maxSubsteps = 4;

        [Min(0f)]
        [Tooltip("If the chain root moves further than this in one frame (metres) the chain is carried along instead of whipping. 0 disables the check.")]
        public float teleportDistance = 1f;

        public void CopyFrom(UMAChainJiggleSettings other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            damping = other.damping;
            elasticity = other.elasticity;
            stiffness = other.stiffness;
            inertia = other.inertia;
            dampingDistribution = CopyCurve(other.dampingDistribution);
            elasticityDistribution = CopyCurve(other.elasticityDistribution);
            stiffnessDistribution = CopyCurve(other.stiffnessDistribution);
            inertiaDistribution = CopyCurve(other.inertiaDistribution);
            gravity = other.gravity;
            restPoseIncludesGravity = other.restPoseIncludesGravity;
            force = other.force;
            freezeAxis = other.freezeAxis;
            radius = other.radius;
            endLength = other.endLength;
            endOffset = other.endOffset;
            blendWeight = other.blendWeight;
            updateRate = other.updateRate;
            maxSubsteps = other.maxSubsteps;
            teleportDistance = other.teleportDistance;
        }

        private static AnimationCurve CopyCurve(AnimationCurve curve)
        {
            return curve != null ? new AnimationCurve(curve.keys) : new AnimationCurve();
        }
    }

    /// <summary>
    /// A sphere or capsule that chain joints are pushed out of.
    /// </summary>
    [Serializable]
    public class UMAChainJiggleCollider
    {
        public enum Direction
        {
            X,
            Y,
            Z
        }

        [Tooltip("Bone the collider follows.")]
        public Transform bone;

        [Tooltip("Centre of the collider in the bone's local space.")]
        public Vector3 center = Vector3.zero;

        [Min(0f)]
        [Tooltip("Radius in metres.")]
        public float radius = 0.08f;

        [Min(0f)]
        [Tooltip("Total height of a capsule in metres. 0 (or less than twice the radius) is a sphere.")]
        public float height = 0f;

        [Tooltip("Local axis a capsule runs along.")]
        public Direction direction = Direction.Y;
    }

    /// <summary>
    /// Bone-chain jiggle for ponytails, pigtails, tails, straps and soft anatomy.
    ///
    /// Each bone joint is a world-space particle. The chain root follows the animation;
    /// every other joint lags behind it, is pulled back to the authored shape, keeps its
    /// bone length exactly, and the bones are then rotated to point at the simulated joints.
    /// Bones are never stretched or moved off their bind offsets.
    ///
    /// Works standalone (add it to the first bone of a chain) or set up on a generated UMA
    /// skeleton by a <see cref="UMAChainJiggleAnimator"/> asset. Either way the component
    /// simulates itself in LateUpdate, after animation.
    /// </summary>
    [DefaultExecutionOrder(11000)]
    [DisallowMultipleComponent]
    public class UMAChainJiggle : MonoBehaviour
    {
        [Tooltip("First bone in the chain. Children are collected recursively. If empty, this transform is used.")]
        public Transform rootBone;

        [Tooltip("Bones to leave alone. Excluded bones and their children are skipped.")]
        public List<Transform> exclusions = new List<Transform>();

        [Tooltip("Spheres and capsules the chain cannot pass through (head, neck, shoulders).")]
        public List<UMAChainJiggleCollider> colliders = new List<UMAChainJiggleCollider>();

        public UMAChainJiggleSettings settings = new UMAChainJiggleSettings();

        private const float Epsilon = 1e-8f;
        private const float ReferenceRate = 60f;
        // Elasticity 1 pulls a joint the whole way back in one 60 Hz step.
        private const float ElasticityToSpring = ReferenceRate * ReferenceRate;

        private struct Particle
        {
            public Transform transform;
            public int parent;
            public int childCount;
            public bool isVirtualEnd;
            public Vector3 endLocalOffset;
            public float chainPosition;

            // Evaluated tuning values.
            public float damping;
            public float spring;
            public float stiffness;
            public float inertia;
            public float dampingFactor;

            // Authored pose, and what was last written so outside changes can be told apart.
            public Quaternion restLocalRotation;
            public Vector3 restLocalPosition;
            public Quaternion appliedLocalRotation;
            public Vector3 appliedLocalPosition;
            public bool rotationApplied;
            public bool positionApplied;

            // Animated pose this frame.
            public Vector3 animPosition;
            public Quaternion animRotation;
            public Vector3 animOffset;
            public Vector3 previousAnimOffset;
            public float restLength;

            // Simulation state.
            public Vector3 position;
            public Vector3 velocity;

            // Shown state: the simulation carried forward to the current frame.
            public Vector3 renderPosition;
        }

        private struct ColliderShape
        {
            public Vector3 a;
            public Vector3 b;
            public float radius;
            public bool isCapsule;
        }

        private Particle[] _particles = new Particle[0];
        private int _particleCount;
        private int _boneCount;
        private ColliderShape[] _colliderShapes = new ColliderShape[0];
        private int _colliderCount;

        private bool _built;
        private bool _needsReset = true;
        private bool _tuningDirty = true;
        private Vector3 _previousRootPosition;
        private Vector3 _simulatedRootPosition;
        private float _accumulator;

        private Transform _gravityReferenceRoot;
        private Vector3 _localRestDown = Vector3.down;

        private UMAData _umaData;
        private int _umaGeneration;
        private bool _ownedByUma;

        private readonly List<Particle> _buildList = new List<Particle>();

        /// <summary>Number of simulated joints, including virtual tips.</summary>
        public int ParticleCount => _particleCount;

        /// <summary>Number of real bones in the chain.</summary>
        public int BoneCount => _boneCount;

        /// <summary>
        /// When false the component does not step itself in LateUpdate; call
        /// <see cref="Simulate"/> yourself.
        /// </summary>
        public bool autoSimulate = true;

        public Transform GetBone(int index)
        {
            int found = 0;
            for (int i = 0; i < _particleCount; i++)
            {
                if (_particles[i].isVirtualEnd)
                {
                    continue;
                }

                if (found == index)
                {
                    return _particles[i].transform;
                }

                found++;
            }

            return null;
        }

        /// <summary>World position of a simulated joint (index below <see cref="ParticleCount"/>).</summary>
        public Vector3 GetParticlePosition(int index)
        {
            return index >= 0 && index < _particleCount ? _particles[index].renderPosition : Vector3.zero;
        }

        private void Start()
        {
            // Standalone use: collect the chain while the rig is still in its scene pose,
            // before the first animation update.
            if (!_built)
            {
                BuildChain();
            }
        }

        private void OnEnable()
        {
            _needsReset = true;
        }

        private void OnDisable()
        {
            RestoreRestPose();
            _needsReset = true;
        }

        private void OnValidate()
        {
            _tuningDirty = true;
        }

        private void LateUpdate()
        {
            if (!autoSimulate)
            {
                return;
            }

            if (_ownedByUma)
            {
                if (_umaData == null || _umaData.EmbeddedPhysicsGeneration != _umaGeneration)
                {
                    // The slot that asked for this chain is no longer part of the character.
                    RestoreRestPose();
                    if (Application.isPlaying)
                    {
                        Destroy(this);
                    }
                    else
                    {
                        DestroyImmediate(this);
                    }

                    return;
                }

                if (!_umaData.BoneAnimatorsEnabled)
                {
                    RestoreRestPose();
                    _needsReset = true;
                    return;
                }
            }

            Simulate(Time.deltaTime);
        }

        /// <summary>
        /// Configure the chain on a generated UMA skeleton. Called by
        /// <see cref="UMAChainJiggleAnimator"/> every time the character is built.
        /// </summary>
        public void Setup(
            Transform chainRoot,
            UMAChainJiggleSettings chainSettings,
            IList<Transform> exclusionTransforms = null,
            IList<UMAChainJiggleCollider> chainColliders = null,
            UMAData umaData = null)
        {
            // Put the bones back first: a rebuild must never read a simulated pose as the authored one.
            RestoreRestPose();

            rootBone = chainRoot;
            // The asset's own settings object is used directly so edits made while playing show at once.
            settings = chainSettings ?? new UMAChainJiggleSettings();

            exclusions.Clear();
            if (exclusionTransforms != null)
            {
                for (int i = 0; i < exclusionTransforms.Count; i++)
                {
                    if (exclusionTransforms[i] != null)
                    {
                        exclusions.Add(exclusionTransforms[i]);
                    }
                }
            }

            colliders.Clear();
            if (chainColliders != null)
            {
                for (int i = 0; i < chainColliders.Count; i++)
                {
                    if (chainColliders[i] != null && chainColliders[i].bone != null)
                    {
                        colliders.Add(chainColliders[i]);
                    }
                }
            }

            _umaData = umaData;
            _ownedByUma = umaData != null;
            _umaGeneration = umaData != null ? umaData.EmbeddedPhysicsGeneration : 0;

            BuildChain();

            if (_boneCount > 0 && _particleCount <= 1)
            {
                string rootName = chainRoot != null ? chainRoot.name : "<null>";
                Debug.LogWarning($"[UMAChainJiggle] Chain root '{rootName}' has no child bones and End Length is 0, so there is nothing to simulate. Choose the first bone of the chain, or set End Length.", this);
            }
        }

        /// <summary>
        /// Collects the bones under <see cref="rootBone"/>. Safe to call again at any time;
        /// the bones are returned to their authored pose first.
        /// </summary>
        public void BuildChain()
        {
            RestoreRestPose();

            _buildList.Clear();
            _boneCount = 0;

            Transform chainRoot = rootBone != null ? rootBone : transform;
            CollectParticles(chainRoot, -1);
            _boneCount = _buildList.Count;
            AddVirtualEnds();

            _particleCount = _buildList.Count;
            if (_particles.Length < _particleCount)
            {
                _particles = new Particle[_particleCount];
            }

            for (int i = 0; i < _particleCount; i++)
            {
                _particles[i] = _buildList[i];
            }

            _buildList.Clear();

            ComputeChainPositions();

            if (_particleCount > 0 && _gravityReferenceRoot != chainRoot)
            {
                // Remember which way is down for the authored pose. The first build of a chain
                // happens with the skeleton in its bind pose.
                Vector3 referenceDown = _umaData != null ? -_umaData.transform.up : Vector3.down;
                _localRestDown = Quaternion.Inverse(chainRoot.rotation) * referenceDown;
                _gravityReferenceRoot = chainRoot;
            }

            _built = true;
            _needsReset = true;
            _tuningDirty = true;
        }

        /// <summary>
        /// Drops all motion and snaps the chain to the authored pose on the next step.
        /// Call after teleporting or re-posing a character.
        /// </summary>
        public void ResetSimulation()
        {
            _needsReset = true;
        }

        /// <summary>
        /// Call after changing <see cref="settings"/> from code so distribution curves are re-read.
        /// </summary>
        public void SettingsChanged()
        {
            _tuningDirty = true;
        }

        private void CollectParticles(Transform bone, int parentIndex)
        {
            if (bone == null || exclusions.Contains(bone))
            {
                return;
            }

            Particle particle = new Particle
            {
                transform = bone,
                parent = parentIndex,
                restLocalRotation = bone.localRotation,
                restLocalPosition = bone.localPosition
            };

            int index = _buildList.Count;
            _buildList.Add(particle);

            if (parentIndex >= 0)
            {
                Particle parent = _buildList[parentIndex];
                parent.childCount++;
                _buildList[parentIndex] = parent;
            }

            for (int i = 0; i < bone.childCount; i++)
            {
                CollectParticles(bone.GetChild(i), index);
            }
        }

        private void AddVirtualEnds()
        {
            UMAChainJiggleSettings s = settings;
            if (s.endLength <= 0f && s.endOffset.sqrMagnitude <= Epsilon)
            {
                return;
            }

            int realCount = _buildList.Count;
            for (int i = 0; i < realCount; i++)
            {
                Particle leaf = _buildList[i];
                if (leaf.childCount > 0)
                {
                    continue;
                }

                Vector3 localOffset = s.endOffset.sqrMagnitude > Epsilon ? s.endOffset : GuessEndOffset(leaf.transform, s.endLength);
                if (localOffset.sqrMagnitude <= Epsilon)
                {
                    continue;
                }

                leaf.childCount = 1;
                _buildList[i] = leaf;

                _buildList.Add(new Particle
                {
                    transform = null,
                    parent = i,
                    isVirtualEnd = true,
                    endLocalOffset = localOffset
                });
            }
        }

        private static Vector3 GuessEndOffset(Transform leaf, float length)
        {
            // Continue in the direction the leaf bone already points: away from its parent.
            Transform parent = leaf.parent;
            if (parent != null)
            {
                Vector3 direction = leaf.position - parent.position;
                if (direction.sqrMagnitude > Epsilon)
                {
                    Vector3 local = leaf.InverseTransformDirection(direction.normalized);
                    Vector3 scale = leaf.lossyScale;
                    float worldPerLocal = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    return local * (worldPerLocal > Epsilon ? length / worldPerLocal : length);
                }
            }

            // UMA bones point along local Y.
            return Vector3.up * length;
        }

        private void ComputeChainPositions()
        {
            // Distance from the root along the bones, 0..1, used by the distribution curves.
            float longest = 0f;
            for (int i = 0; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.parent < 0)
                {
                    p.chainPosition = 0f;
                    continue;
                }

                ref Particle parent = ref _particles[p.parent];
                Vector3 parentPosition = parent.transform.position;
                Vector3 position = p.isVirtualEnd ? parent.transform.TransformPoint(p.endLocalOffset) : p.transform.position;
                p.chainPosition = parent.chainPosition + Vector3.Distance(parentPosition, position);
                longest = Mathf.Max(longest, p.chainPosition);
            }

            for (int i = 0; i < _particleCount; i++)
            {
                _particles[i].chainPosition = longest > Epsilon ? _particles[i].chainPosition / longest : 0f;
            }
        }

        private void RefreshTuning()
        {
            UMAChainJiggleSettings s = settings;
            for (int i = 0; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                float t = p.chainPosition;
                p.damping = Mathf.Clamp01(s.damping * Evaluate(s.dampingDistribution, t));
                p.spring = Mathf.Clamp01(s.elasticity * Evaluate(s.elasticityDistribution, t)) * ElasticityToSpring;
                p.stiffness = Mathf.Clamp01(s.stiffness * Evaluate(s.stiffnessDistribution, t));
                p.inertia = Mathf.Clamp01(s.inertia * Evaluate(s.inertiaDistribution, t));
            }

            _tuningDirty = false;
        }

        private static float Evaluate(AnimationCurve curve, float t)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(t) : 1f;
        }

        /// <summary>
        /// Runs one frame of simulation and poses the bones. Called from LateUpdate when
        /// <see cref="autoSimulate"/> is on; call it yourself (after animation) otherwise.
        /// </summary>
        public void Simulate(float deltaTime)
        {
            if (settings == null)
            {
                settings = new UMAChainJiggleSettings();
            }

            if (!_built || ChainIsBroken())
            {
                BuildChain();
            }

            if (_particleCount <= 1)
            {
                return;
            }

#if UNITY_EDITOR
            // Sliders can be dragged while playing.
            _tuningDirty = true;
#endif
            if (_tuningDirty)
            {
                RefreshTuning();
            }

            PrepareRestPose();
            ReadAnimatedPose();

            if (_needsReset || !IsFinite(_particles[_particleCount - 1].position))
            {
                SnapToAnimatedPose();
            }
            else
            {
                Step(deltaTime);
            }

            ApplyToBones();
        }

        private bool ChainIsBroken()
        {
            for (int i = 0; i < _particleCount; i++)
            {
                if (!_particles[i].isVirtualEnd && _particles[i].transform == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        private static bool Same(Quaternion a, Quaternion b)
        {
            const float tolerance = 1e-6f;
            return Mathf.Abs(a.x - b.x) <= tolerance && Mathf.Abs(a.y - b.y) <= tolerance && Mathf.Abs(a.z - b.z) <= tolerance && Mathf.Abs(a.w - b.w) <= tolerance;
        }

        private static bool Same(Vector3 a, Vector3 b)
        {
            const float tolerance = 1e-7f;
            return Mathf.Abs(a.x - b.x) <= tolerance && Mathf.Abs(a.y - b.y) <= tolerance && Mathf.Abs(a.z - b.z) <= tolerance;
        }

        /// <summary>
        /// Undo last frame's output, unless something else has written the bone since (the
        /// Animator, a UMA rebuild, DNA). Whatever pose is left is this frame's authored pose.
        /// </summary>
        private void PrepareRestPose()
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                Transform bone = p.transform;
                if (bone == null)
                {
                    continue;
                }

                if (p.rotationApplied)
                {
                    if (Same(bone.localRotation, p.appliedLocalRotation))
                    {
                        bone.localRotation = p.restLocalRotation;
                    }

                    p.rotationApplied = false;
                }

                if (p.positionApplied)
                {
                    if (Same(bone.localPosition, p.appliedLocalPosition))
                    {
                        bone.localPosition = p.restLocalPosition;
                    }

                    p.positionApplied = false;
                }

                p.restLocalRotation = bone.localRotation;
                p.restLocalPosition = bone.localPosition;
            }
        }

        private void RestoreRestPose()
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                Transform bone = p.transform;
                if (bone == null)
                {
                    continue;
                }

                if (p.rotationApplied && Same(bone.localRotation, p.appliedLocalRotation))
                {
                    bone.localRotation = p.restLocalRotation;
                }

                if (p.positionApplied && Same(bone.localPosition, p.appliedLocalPosition))
                {
                    bone.localPosition = p.restLocalPosition;
                }

                p.rotationApplied = false;
                p.positionApplied = false;
            }
        }

        private void ReadAnimatedPose()
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.isVirtualEnd)
                {
                    ref Particle leaf = ref _particles[p.parent];
                    p.animPosition = leaf.transform.TransformPoint(p.endLocalOffset);
                    p.animRotation = leaf.animRotation;
                }
                else
                {
                    p.transform.GetPositionAndRotation(out p.animPosition, out p.animRotation);
                }

                if (p.parent >= 0)
                {
                    p.animOffset = p.animPosition - _particles[p.parent].animPosition;
                    p.restLength = p.animOffset.magnitude;
                }
            }

            int shapeCount = colliders != null ? colliders.Count : 0;
            if (_colliderShapes.Length < shapeCount)
            {
                _colliderShapes = new ColliderShape[shapeCount];
            }

            _colliderCount = 0;
            for (int i = 0; i < shapeCount; i++)
            {
                UMAChainJiggleCollider collider = colliders[i];
                if (collider == null || collider.bone == null || collider.radius <= 0f)
                {
                    continue;
                }

                Transform bone = collider.bone;
                Vector3 lossy = bone.lossyScale;
                float scale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
                float half = collider.height * 0.5f - collider.radius;

                ColliderShape shape = new ColliderShape { radius = collider.radius * scale };
                if (half > 0f)
                {
                    Vector3 axis = collider.direction == UMAChainJiggleCollider.Direction.X ? Vector3.right
                        : collider.direction == UMAChainJiggleCollider.Direction.Y ? Vector3.up : Vector3.forward;
                    shape.a = bone.TransformPoint(collider.center - axis * half);
                    shape.b = bone.TransformPoint(collider.center + axis * half);
                    shape.isCapsule = true;
                }
                else
                {
                    shape.a = bone.TransformPoint(collider.center);
                    shape.b = shape.a;
                }

                _colliderShapes[_colliderCount++] = shape;
            }
        }

        private void SnapToAnimatedPose()
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                p.position = p.animPosition;
                p.renderPosition = p.animPosition;
                p.velocity = Vector3.zero;
                p.previousAnimOffset = p.animOffset;
            }

            _previousRootPosition = _particles[0].animPosition;
            _simulatedRootPosition = _previousRootPosition;
            _accumulator = 0f;
            _needsReset = false;
        }

        private float RootScale()
        {
            Vector3 lossy = _particles[0].transform.lossyScale;
            return Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
        }

        /// <summary>
        /// Advances the chain by <paramref name="deltaTime"/>. The simulation always runs in
        /// steps of 1 / updateRate so it behaves the same at any frame rate: slow frames run
        /// several steps, fast frames run none and show the last state carried forward.
        /// </summary>
        private void Step(float deltaTime)
        {
            UMAChainJiggleSettings s = settings;
            Vector3 rootNow = _particles[0].animPosition;
            Vector3 rootBefore = _previousRootPosition;
            float scale = RootScale();

            int steps = 0;
            bool spreadEvenly = false;
            float accumulatedBefore = _accumulator;
            float h = 1f / Mathf.Clamp(s.updateRate, 60, 240);

            Vector3 frameMove = rootNow - rootBefore;
            bool teleported = s.teleportDistance > 0f && frameMove.sqrMagnitude > s.teleportDistance * s.teleportDistance * scale * scale;
            if (teleported)
            {
                // Carry the whole chain along; it keeps its shape and its motion.
                for (int i = 1; i < _particleCount; i++)
                {
                    _particles[i].position += frameMove;
                }

                _simulatedRootPosition += frameMove;
                rootBefore = rootNow;
            }
            else if (deltaTime > 1e-6f)
            {
                int maxSubsteps = Mathf.Clamp(s.maxSubsteps, 1, 8);
                _accumulator += deltaTime;
                steps = Mathf.FloorToInt(_accumulator / h + 1e-3f);
                if (steps > maxSubsteps)
                {
                    // Too slow to keep up: run what is allowed across the whole frame and drop the rest.
                    steps = maxSubsteps;
                    spreadEvenly = true;
                    _accumulator = 0f;
                }
                else
                {
                    _accumulator = Mathf.Max(0f, _accumulator - steps * h);
                }
            }

            if (steps > 0)
            {
                Vector3 acceleration = Vector3.down * s.gravity;
                if (s.restPoseIncludesGravity)
                {
                    // The authored pose already balances gravity along the direction that was down
                    // when it was modelled, so only the difference acts on the chain.
                    Vector3 restDown = _particles[0].animRotation * _localRestDown;
                    acceleration = (Vector3.down - restDown) * s.gravity;
                }

                acceleration = (acceleration + s.force) * scale;

                float particleRadius = Mathf.Max(0f, s.radius) * scale;
                int freezeAxis = (int)s.freezeAxis - 1;
                float stepsAtReference = h * ReferenceRate;

                for (int i = 1; i < _particleCount; i++)
                {
                    _particles[i].dampingFactor = Mathf.Pow(1f - _particles[i].damping, stepsAtReference);
                }

                for (int step = 1; step <= steps; step++)
                {
                    // Where this step falls inside the frame, so the root and the authored
                    // directions are fed in smoothly instead of in one jump per frame.
                    float t = spreadEvenly ? step / (float)steps : Mathf.Clamp01((step * h - accumulatedBefore) / deltaTime);
                    Vector3 rootPosition = Vector3.LerpUnclamped(rootBefore, rootNow, t);
                    Vector3 rootMove = rootPosition - _simulatedRootPosition;
                    _simulatedRootPosition = rootPosition;
                    _particles[0].position = rootPosition;

                    for (int i = 1; i < _particleCount; i++)
                    {
                        ref Particle p = ref _particles[i];
                        ref Particle parent = ref _particles[p.parent];

                        if (p.restLength <= 1e-6f)
                        {
                            // A bone with no length sits on its parent.
                            p.position = parent.position;
                            p.velocity = Vector3.zero;
                            continue;
                        }

                        Vector3 offset = p.animOffset;
                        if (t < 1f)
                        {
                            offset = Vector3.Lerp(p.previousAnimOffset, p.animOffset, t);
                            float offsetLength = offset.magnitude;
                            offset = offsetLength > 1e-6f ? offset * (p.restLength / offsetLength) : p.animOffset;
                        }

                        Vector3 restPosition = parent.position + offset;
                        Vector3 start = p.position + rootMove * p.inertia;
                        Vector3 position = start;

                        Vector3 velocity = p.velocity * p.dampingFactor;
                        velocity += (acceleration + (restPosition - position) * p.spring) * h;
                        position += velocity * h;

                        if (p.stiffness > 0f)
                        {
                            Vector3 toRest = restPosition - position;
                            float distance = toRest.magnitude;
                            float allowed = p.restLength * (1f - p.stiffness) * 2f;
                            if (distance > allowed)
                            {
                                position += toRest * ((distance - allowed) / distance);
                            }
                        }

                        if (freezeAxis >= 0)
                        {
                            Vector3 normal = parent.animRotation * (freezeAxis == 0 ? Vector3.right : freezeAxis == 1 ? Vector3.up : Vector3.forward);
                            position -= normal * Vector3.Dot(position - restPosition, normal);
                        }

                        // Push out of colliders, then put the bone back to its length. Fixing the
                        // length can push a joint back into a collider, so a joint that touched
                        // one gets a few more passes. Length is fixed last: bones never stretch.
                        for (int pass = 0; pass < 4; pass++)
                        {
                            bool touched = false;
                            for (int c = 0; c < _colliderCount; c++)
                            {
                                touched |= Collide(ref position, particleRadius, ref _colliderShapes[c]);
                            }

                            Vector3 fromParent = position - parent.position;
                            float length = fromParent.magnitude;
                            position = length > 1e-6f ? parent.position + fromParent * (p.restLength / length) : restPosition;
                            if (!touched)
                            {
                                break;
                            }
                        }

                        p.velocity = (position - start) / h;
                        p.position = position;
                    }
                }
            }

            // What is shown: the last simulated state carried forward to now, hung from where the
            // root really is this frame, with exact bone lengths.
            float ahead = _accumulator;
            Vector3 carried = rootNow - _simulatedRootPosition;
            _particles[0].renderPosition = rootNow;
            for (int i = 1; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                Vector3 parentPosition = _particles[p.parent].renderPosition;
                if (p.restLength <= 1e-6f)
                {
                    p.renderPosition = parentPosition;
                    continue;
                }

                Vector3 fromParent = p.position + p.velocity * ahead + carried * p.inertia - parentPosition;
                float length = fromParent.magnitude;
                p.renderPosition = length > 1e-6f ? parentPosition + fromParent * (p.restLength / length) : parentPosition + p.animOffset;
            }

            for (int i = 1; i < _particleCount; i++)
            {
                _particles[i].previousAnimOffset = _particles[i].animOffset;
            }

            _previousRootPosition = rootNow;
        }

        private static bool Collide(ref Vector3 position, float particleRadius, ref ColliderShape shape)
        {
            Vector3 closest = shape.a;
            if (shape.isCapsule)
            {
                Vector3 axis = shape.b - shape.a;
                float axisSqr = axis.sqrMagnitude;
                float along = axisSqr > Epsilon ? Mathf.Clamp01(Vector3.Dot(position - shape.a, axis) / axisSqr) : 0f;
                closest = shape.a + axis * along;
            }

            Vector3 away = position - closest;
            float reach = shape.radius + particleRadius;
            float sqrDistance = away.sqrMagnitude;
            if (sqrDistance < reach * reach && sqrDistance > Epsilon)
            {
                position = closest + away * (reach / Mathf.Sqrt(sqrDistance));
                return true;
            }

            return false;
        }

        private void ApplyToBones()
        {
            float weight = Mathf.Clamp01(settings.blendWeight);
            if (weight <= 0f)
            {
                return;
            }

            for (int i = 1; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                ref Particle parent = ref _particles[p.parent];
                Transform parentBone = parent.transform;

                if (parent.childCount == 1)
                {
                    // Aim the parent bone at this joint. Bones with several children keep
                    // their orientation, as there is no single direction to aim at.
                    //
                    // A bone with no length sits exactly on its own parent, and skin is usually
                    // weighted to both (UMA's LeftOuterBreast and LeftInnerBreast). Turning only
                    // the inner one would move half the skin, so the turn goes to the bone above.
                    int aimIndex = p.parent;
                    while (_particles[aimIndex].parent >= 0
                        && _particles[aimIndex].restLength <= 1e-6f
                        && _particles[_particles[aimIndex].parent].childCount == 1)
                    {
                        aimIndex = _particles[aimIndex].parent;
                    }

                    ref Particle aim = ref _particles[aimIndex];
                    Transform aimBone = aim.transform;
                    Vector3 joint = p.isVirtualEnd ? parentBone.TransformPoint(p.endLocalOffset) : p.transform.position;
                    Vector3 current = joint - aimBone.position;
                    Vector3 wanted = p.renderPosition - aim.renderPosition;
                    if (current.sqrMagnitude > Epsilon && wanted.sqrMagnitude > Epsilon)
                    {
                        Quaternion turn = Quaternion.FromToRotation(current, wanted);
                        if (weight < 1f)
                        {
                            turn = Quaternion.Slerp(Quaternion.identity, turn, weight);
                        }

                        aimBone.rotation = turn * aimBone.rotation;
                        aim.appliedLocalRotation = aimBone.localRotation;
                        aim.rotationApplied = true;
                    }
                }
                else if (!p.isVirtualEnd)
                {
                    Transform bone = p.transform;
                    bone.position = weight < 1f ? Vector3.Lerp(bone.position, p.renderPosition, weight) : p.renderPosition;
                    p.appliedLocalPosition = bone.localPosition;
                    p.positionApplied = true;
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_built || _particleCount == 0 || _needsReset)
            {
                return;
            }

            float radius = Mathf.Max(settings.radius * RootScale(), 0.004f);
            for (int i = 0; i < _particleCount; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.parent >= 0)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(_particles[p.parent].renderPosition, p.renderPosition);
                    Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                    Gizmos.DrawLine(_particles[p.parent].animPosition, p.animPosition);
                }

                Gizmos.color = p.parent < 0 ? Color.green : p.isVirtualEnd ? new Color(1f, 0.5f, 0f) : Color.yellow;
                Gizmos.DrawWireSphere(p.renderPosition, radius);
            }

            Gizmos.color = new Color(1f, 0.2f, 0.6f);
            for (int i = 0; i < _colliderCount; i++)
            {
                Gizmos.DrawWireSphere(_colliderShapes[i].a, _colliderShapes[i].radius);
                if (_colliderShapes[i].isCapsule)
                {
                    Gizmos.DrawWireSphere(_colliderShapes[i].b, _colliderShapes[i].radius);
                    Gizmos.DrawLine(_colliderShapes[i].a, _colliderShapes[i].b);
                }
            }
        }
#endif
    }
}
