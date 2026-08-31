using System;
using System.Collections.Generic;
using UMA.Dynamics;
using UMA.Dynamics.Examples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace UMA.Examples
{
    /// <summary>
    /// Lightweight crowd movement for the random-character sample. This is
    /// intentionally not a navigation solution: walkers stay on their spawn
    /// plane, keep inside a local radius, and avoid one another cooperatively.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UMA/Examples/Random Character Walker")]
    public sealed class RandomCharacterWalker : MonoBehaviour, IUMAShooterTarget
    {
        private enum Activity
        {
            Walking,
            Paused
        }

        private static readonly List<RandomCharacterWalker> Walkers = new();

        [Header("Movement")]
        [FormerlySerializedAs("moveSpeed")]
        [Tooltip("Animator playback rate while walking. This scales both extracted " +
            "root motion and the in-place animation fallback speed.")]
        [Min(0.05f)] public float animationPlaybackSpeed = 0.75f;
        [Tooltip("Travel speed in meters per second at normal Animator playback when " +
            "the active locomotion clip contains no horizontal root motion.")]
        [Min(0f)] public float inPlaceMovementSpeed = 1.4f;
        [Tooltip("Minimum horizontal root-motion speed, in meters per second, that is " +
            "treated as intentional locomotion. Slower motion is usually in-place " +
            "animation drift and uses In Place Movement Speed instead.")]
        [Min(0f)] public float minimumRootMotionSpeed = 0.25f;
        [Min(0.1f)] public float maximumSpawnDistance = 2.5f;
        [Min(1f)] public float turnSpeed = 150f;
        [Range(0f, 20f)] public float returnAngleVariation = 20f;

        [Header("Personality")]
        public Vector2 headingChangeInterval = new(1.5f, 4f);
        [Range(0f, 90f)] public float randomHeadingVariation = 35f;
        [Range(0f, 30f)] public float headingNoise = 10f;
        [Min(0.01f)] public float headingNoiseFrequency = 0.18f;
        public Vector2 pauseInterval = new(4f, 10f);
        public Vector2 pauseDuration = new(0.8f, 2.4f);
        [Range(0f, 45f)] public float pauseLookVariation = 20f;

        [Header("Crowd Avoidance")]
        [Min(0.05f)] public float personalSpace = 0.55f;
        [Min(0.05f)] public float lookAheadDistance = 1.25f;
        [Min(0.02f)] public float avoidanceCheckInterval = 0.12f;
        public Vector2 avoidanceTurn = new(70f, 130f);
        [Min(0.1f)] public float avoidanceCommitDuration = 0.75f;
        [Min(0.01f)] public float overlapSeparationSpeed = 0.8f;
        [FormerlySerializedAs("bumpPauseDuration")]
        public Vector2 bumpReactionCooldown = new(0.4f, 0.8f);

        [Header("Stall Recovery")]
        [Min(0.25f)] public float progressCheckInterval = 1f;
        [Min(0.001f)] public float minimumProgressDistance = 0.025f;
        [Min(1)] public int stalledChecksBeforeRecovery = 2;

        [Header("Animation")]
        public string speedParameter = "Speed";
        public string directionParameter = "Direction";
        [Min(0f)] public float walkingAnimationSpeed = 0.45f;
        [Min(0f)] public float animationDamping = 0.12f;
        [Tooltip("Optional Animator trigger parameters used at random when pausing.")]
        public string[] pauseAnimationTriggers = Array.Empty<string>();
        [Tooltip("Optional Animator trigger parameters used at random after contact.")]
        public string[] bumpAnimationTriggers = Array.Empty<string>();

        [Header("Optional Sound")]
        public AudioClip[] bumpSounds = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float bumpVolume = 0.35f;

        [Header("Combat")]
        [Tooltip("Number of ordinary body shots required to ragdoll this character. " +
            "Head and groin shots remain immediately lethal.")]
        [Min(1)] public int maximumHealth = 3;
        [SerializeField, Min(0), Tooltip("Remaining ordinary hits at runtime.")]
        private int currentHealth = 3;
        [Min(0.1f), Tooltip("How long, in seconds, the walker pursues its attacker.")]
        public float pursuitDuration = 15f;
        [Min(0.1f), Tooltip("Distance in meters at which the walker stops advancing.")]
        public float pursuitStoppingDistance = 1.25f;

        public int CurrentHealth => currentHealth;
        public Transform CurrentCombatTarget => combatTarget;
        public bool IsPursuingCombatTarget => isPursuingCombatTarget;

        private Vector3 spawnPosition;
        private Vector3 desiredDirection;
        private System.Random random;
        private Animator animator;
        private RuntimeAnimatorController observedController;
        private Rigidbody movementBody;
        private Vector3 requestedPhysicsVelocity;
        private bool hasPhysicsLocomotionRequest;
        private AudioSource audioSource;
        private Activity activity;
        private float activityUntil;
        private float nextHeadingChange;
        private float nextPause;
        private float nextAvoidanceCheck;
        private float nextAnimatorSearch;
        private float nextMovementBodySearch;
        private float avoidanceCommitUntil;
        private float nextBumpReaction;
        private float nextProgressCheck;
        private float noiseOffset;
        private Vector3 progressSamplePosition;
        private int stalledCheckCount;
        private bool returningHome;
        private bool hasSpeedParameter;
        private bool hasDirectionParameter;
        private int speedParameterHash;
        private int directionParameterHash;
        private bool initialized;
        private Transform combatTarget;
        private float combatTargetUntil;
        private bool isPursuingCombatTarget;
        private bool shooterRagdolled;
        private UMAPhysicsAvatar physicsAvatar;
        private bool ragdollEventsSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWalkers()
        {
            Walkers.Clear();
        }

        private void Awake()
        {
            ResetShotHealth();
            EnsureRagdollSubscription();
        }

        private void OnEnable()
        {
            if (Application.isPlaying && !Walkers.Contains(this))
            {
                Walkers.Add(this);
            }
        }

        private void OnDisable()
        {
            Walkers.Remove(this);
            StopPhysicsLocomotion();
        }

        private void OnDestroy()
        {
            RemoveRagdollSubscription();
        }

        private void Start()
        {
            EnsureRagdollSubscription();
            spawnPosition = transform.position;
            random = new System.Random(CreateStableSeed(gameObject.name, spawnPosition));
            noiseOffset = Range(0f, 1000f);

            Vector3 initialForward = Flatten(transform.forward);
            desiredDirection = initialForward.sqrMagnitude > 0.001f
                ? initialForward.normalized
                : Vector3.forward;

            float now = Time.time;
            nextHeadingChange = now + Range(headingChangeInterval);
            nextPause = now + Range(pauseInterval);
            nextAvoidanceCheck = now + Range(0f, avoidanceCheckInterval);
            nextProgressCheck = now + progressCheckInterval;
            progressSamplePosition = transform.position;
            RefreshAnimator();
            RefreshMovementBody();
            initialized = true;
        }

        public bool ApplyShot(Transform attacker, bool lethalHit)
        {
            EnsureRagdollSubscription();
            if (attacker != null && attacker != transform)
            {
                combatTarget = attacker;
                combatTargetUntil = Time.time + Mathf.Max(0.1f, pursuitDuration);
                activity = Activity.Walking;
                returningHome = false;
                Vector3 towardAttacker = Flatten(attacker.position - transform.position);
                if (towardAttacker.sqrMagnitude > 0.001f)
                    desiredDirection = towardAttacker.normalized;
            }

            if (lethalHit)
                currentHealth = 0;
            else
                currentHealth = Mathf.Max(0, currentHealth - 1);

            return lethalHit || currentHealth == 0;
        }

        public void SetShooterRagdolled(bool ragdolled)
        {
            if (ragdolled)
            {
                if (shooterRagdolled)
                    return;

                shooterRagdolled = true;
                isPursuingCombatTarget = false;
                UpdateAnimation(0f);
                enabled = false;
                return;
            }

            if (!shooterRagdolled)
                return;

            shooterRagdolled = false;
            ResetShotHealth();
            activity = Activity.Walking;
            enabled = true;
        }

        public void ResetShotHealth()
        {
            currentHealth = Mathf.Max(1, maximumHealth);
        }

        private void EnsureRagdollSubscription()
        {
            if (ragdollEventsSubscribed && physicsAvatar != null)
                return;

            RemoveRagdollSubscription();
            physicsAvatar = GetComponentInParent<UMAPhysicsAvatar>();
            if (physicsAvatar == null)
                physicsAvatar = GetComponentInChildren<UMAPhysicsAvatar>(true);
            if (physicsAvatar == null)
                return;

            physicsAvatar.onRagdollStarted ??= new UnityEvent();
            physicsAvatar.onRagdollEnded ??= new UnityEvent();
            physicsAvatar.onRagdollStarted.AddListener(HandleRagdollStarted);
            physicsAvatar.onRagdollEnded.AddListener(HandleRagdollEnded);
            ragdollEventsSubscribed = true;
        }

        private void RemoveRagdollSubscription()
        {
            if (ragdollEventsSubscribed && physicsAvatar != null)
            {
                physicsAvatar.onRagdollStarted?.RemoveListener(HandleRagdollStarted);
                physicsAvatar.onRagdollEnded?.RemoveListener(HandleRagdollEnded);
            }
            ragdollEventsSubscribed = false;
            physicsAvatar = null;
        }

        private void HandleRagdollStarted()
        {
            SetShooterRagdolled(true);
        }

        private void HandleRagdollEnded()
        {
            SetShooterRagdolled(false);
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            float now = Time.time;
            if ((animator == null && now >= nextAnimatorSearch) ||
                (animator != null && animator.runtimeAnimatorController !=
                    observedController))
            {
                RefreshAnimator();
            }

            if (UpdateCombatPursuit(now))
                return;

            if (now >= nextAvoidanceCheck)
            {
                nextAvoidanceCheck = now +
                    avoidanceCheckInterval * Range(0.75f, 1.25f);
                RandomCharacterWalker threat =
                    FindNearestThreat(out bool touching);
                if (threat != null)
                {
                    if (touching)
                    {
                        ResolveOverlap(threat);
                        PlayBumpReaction(now);
                    }
                    if (activity == Activity.Walking &&
                        now >= avoidanceCommitUntil)
                    {
                        Avoid(threat);
                    }
                }
            }

            if (activity == Activity.Paused)
            {
                TurnToward(desiredDirection, turnSpeed * 1.25f);
                UpdateAnimation(0f);
                if (now < activityUntil)
                {
                    return;
                }

                activity = Activity.Walking;
                if (DistanceFromSpawnSquared() >= ReturnReleaseDistanceSquared())
                {
                    ChooseReturnDirection();
                }
                else
                {
                    ChooseWanderDirection(randomHeadingVariation);
                }
            }

            float distanceSquared = DistanceFromSpawnSquared();
            float maximumDistance = Mathf.Max(0.1f, maximumSpawnDistance);
            if (!returningHome && distanceSquared >= maximumDistance * maximumDistance)
            {
                ChooseReturnDirection();
            }
            else if (returningHome && distanceSquared <= ReturnReleaseDistanceSquared())
            {
                returningHome = false;
                ChooseWanderDirection(randomHeadingVariation * 0.5f);
            }

            if (!returningHome && now >= nextPause &&
                now >= avoidanceCommitUntil)
            {
                BeginPause();
                UpdateAnimation(0f);
                return;
            }

            if (!returningHome && now >= nextHeadingChange &&
                now >= avoidanceCommitUntil)
            {
                ChooseWanderDirection(randomHeadingVariation);
            }

            Vector3 steeringDirection = desiredDirection;
            if (!returningHome && now >= avoidanceCommitUntil &&
                headingNoise > 0f)
            {
                float noise = Mathf.PerlinNoise(noiseOffset,
                    now * headingNoiseFrequency) * 2f - 1f;
                steeringDirection = Quaternion.AngleAxis(
                    noise * headingNoise, Vector3.up) * desiredDirection;
            }

            TurnToward(steeringDirection, turnSpeed);
            UpdateAnimation(walkingAnimationSpeed);
            CheckForStall(now);
        }

        private void FixedUpdate()
        {
            if (!hasPhysicsLocomotionRequest || movementBody == null ||
                movementBody.isKinematic)
            {
                return;
            }

            // Apply locomotion on the physics clock. Only the horizontal axes
            // belong to the walker; gravity and collision response retain full
            // control of vertical velocity so elevated characters remain
            // grounded on platforms and ragdolls can fall naturally.
            Vector3 velocity = movementBody.linearVelocity;
            velocity.x = requestedPhysicsVelocity.x;
            velocity.z = requestedPhysicsVelocity.z;
            movementBody.linearVelocity = velocity;
        }

        private bool UpdateCombatPursuit(float now)
        {
            if (combatTarget == null || now >= combatTargetUntil)
            {
                bool shouldReturnHome = isPursuingCombatTarget;
                combatTarget = null;
                isPursuingCombatTarget = false;
                if (shouldReturnHome)
                    ChooseReturnDirection();
                return false;
            }

            isPursuingCombatTarget = true;
            returningHome = false;
            nextPause = combatTargetUntil + Range(pauseInterval);
            Vector3 towardTarget = Flatten(combatTarget.position - transform.position);
            float stoppingDistance = Mathf.Max(0.1f, pursuitStoppingDistance);
            if (towardTarget.sqrMagnitude > stoppingDistance * stoppingDistance)
            {
                desiredDirection = towardTarget.normalized;
                activity = Activity.Walking;
                TurnToward(desiredDirection, turnSpeed);
                UpdateAnimation(walkingAnimationSpeed);
            }
            else
            {
                if (towardTarget.sqrMagnitude > 0.001f)
                    desiredDirection = towardTarget.normalized;
                activity = Activity.Paused;
                TurnToward(desiredDirection, turnSpeed * 1.25f);
                UpdateAnimation(0f);
            }
            return true;
        }

        private void OnAnimatorMove()
        {
            if (!initialized || animator == null)
            {
                return;
            }

            if (movementBody == null && Time.time >= nextMovementBodySearch)
                RefreshMovementBody();

            if (activity != Activity.Walking)
            {
                StopPhysicsLocomotion();
                return;
            }

            // Steering owns yaw. Prefer authored root translation, but keep
            // in-place locomotion clips useful by supplying a physical walking
            // speed when their evaluated horizontal delta is effectively zero.
            Vector3 locomotionDelta = ResolveLocomotionDelta(
                animator.deltaPosition, Time.deltaTime);
            if (isPursuingCombatTarget)
                ApplyPursuitRootMotion(locomotionDelta);
            else
                ApplyRootMotionWithinSpawnRadius(locomotionDelta);
        }

        private Vector3 ResolveLocomotionDelta(Vector3 animatorDelta,
            float deltaTime)
        {
            Vector3 horizontalDelta = Flatten(animatorDelta);
            if (deltaTime <= 0f || inPlaceMovementSpeed <= 0f)
            {
                return horizontalDelta;
            }

            // Unity units are meters. Some nominally in-place clips contain a
            // tiny root translation from compression or import sampling. A
            // nonzero test mistakes that drift for locomotion, causing the
            // walker to move millimeters, trip stall recovery, and continually
            // turn. Classify root motion by its real-world speed instead.
            float rootMotionSpeed = horizontalDelta.magnitude / deltaTime;
            if (rootMotionSpeed > Mathf.Max(0f, minimumRootMotionSpeed))
                return horizontalDelta;

            Vector3 forward = Flatten(transform.forward);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = desiredDirection;
            if (forward.sqrMagnitude <= 0.0001f)
                return Vector3.zero;

            float playbackRate = Mathf.Max(0.05f, animationPlaybackSpeed);
            return forward.normalized *
                (inPlaceMovementSpeed * playbackRate * deltaTime);
        }

        private RandomCharacterWalker FindNearestThreat(out bool touching)
        {
            touching = false;
            RandomCharacterWalker nearest = null;
            float nearestDistanceSquared = float.MaxValue;
            Vector3 position = transform.position;
            Vector3 forward = Flatten(transform.forward).normalized;
            float contactDistanceSquared = personalSpace * personalSpace;
            float aheadDistanceSquared = lookAheadDistance * lookAheadDistance;

            for (int i = Walkers.Count - 1; i >= 0; i--)
            {
                RandomCharacterWalker other = Walkers[i];
                if (other == null)
                {
                    Walkers.RemoveAt(i);
                    continue;
                }
                if (ReferenceEquals(other, this) || !other.isActiveAndEnabled)
                {
                    continue;
                }

                Vector3 offset = Flatten(other.transform.position - position);
                float distanceSquared = offset.sqrMagnitude;
                bool isTouching = distanceSquared < contactDistanceSquared;
                bool isAhead = false;
                if (!isTouching && distanceSquared < aheadDistanceSquared &&
                    distanceSquared > 0.0001f)
                {
                    float forwardDistance = Vector3.Dot(forward, offset);
                    Vector3 lateral = offset - forward * forwardDistance;
                    isAhead = forwardDistance > 0f &&
                        lateral.sqrMagnitude < contactDistanceSquared * 1.5f;
                }

                if ((isTouching || isAhead) && distanceSquared < nearestDistanceSquared)
                {
                    nearest = other;
                    nearestDistanceSquared = distanceSquared;
                    touching = isTouching;
                }
            }

            return nearest;
        }

        private void Avoid(RandomCharacterWalker other)
        {
            Vector3 away = Flatten(transform.position - other.transform.position);
            if (away.sqrMagnitude < 0.001f)
            {
                away = Quaternion.AngleAxis(Range(-90f, 90f), Vector3.up) *
                    transform.right;
            }

            away.Normalize();
            float side = Vector3.Dot(transform.right, away) < 0f ? -1f : 1f;
            Vector3 turnedForward = Quaternion.AngleAxis(
                Range(avoidanceTurn) * side, Vector3.up) *
                Flatten(transform.forward).normalized;
            desiredDirection = (away * 1.5f + turnedForward).normalized;
            returningHome = false;
            avoidanceCommitUntil = Time.time + avoidanceCommitDuration;
            nextHeadingChange = avoidanceCommitUntil +
                Range(headingChangeInterval);
        }

        private void ResolveOverlap(RandomCharacterWalker other)
        {
            Vector3 away = Flatten(transform.position - other.transform.position);
            float distance = away.magnitude;
            if (distance < 0.001f)
            {
                away = Quaternion.AngleAxis(Range(0f, 360f), Vector3.up) *
                    Vector3.forward;
                distance = 0f;
            }
            else
            {
                away /= distance;
            }

            // This is depenetration, not locomotion. The small correction
            // prevents a crowd from forming an overlap that normal travel
            // cannot resolve.
            float overlap = Mathf.Max(0f, personalSpace - distance);
            float correction = Mathf.Min(overlap * 0.5f,
                overlapSeparationSpeed * avoidanceCheckInterval);
            ApplyPositionWithinSpawnRadius(
                transform.position + away * correction);
        }

        private void PlayBumpReaction(float now)
        {
            if (now < nextBumpReaction)
            {
                return;
            }

            nextBumpReaction = now + Range(bumpReactionCooldown);
            PlayRandomTrigger(bumpAnimationTriggers);
            PlayBumpSound();
        }

        private void CheckForStall(float now)
        {
            if (now < nextProgressCheck)
            {
                return;
            }

            float progress = Flatten(
                transform.position - progressSamplePosition).magnitude;
            progressSamplePosition = transform.position;
            nextProgressCheck = now + progressCheckInterval;
            if (progress >= minimumProgressDistance)
            {
                stalledCheckCount = 0;
                return;
            }

            stalledCheckCount++;
            if (stalledCheckCount < stalledChecksBeforeRecovery)
            {
                return;
            }

            stalledCheckCount = 0;
            RecoverFromStall(now);
        }

        private void RecoverFromStall(float now)
        {
            Vector3 fromSpawn = Flatten(transform.position - spawnPosition);
            float radius = Mathf.Max(0.1f, maximumSpawnDistance);
            if (fromSpawn.sqrMagnitude > radius * radius * 0.64f)
            {
                ChooseReturnDirection();
            }
            else
            {
                desiredDirection = Quaternion.AngleAxis(
                    Range(120f, 240f), Vector3.up) *
                    Flatten(transform.forward).normalized;
                returningHome = false;
            }

            avoidanceCommitUntil = now + avoidanceCommitDuration * 1.5f;
            nextPause = avoidanceCommitUntil + Range(pauseInterval);
        }

        private void BeginPause()
        {
            activity = Activity.Paused;
            activityUntil = Time.time + Range(pauseDuration);
            nextPause = activityUntil + Range(pauseInterval);
            desiredDirection = Quaternion.AngleAxis(
                Range(-pauseLookVariation, pauseLookVariation), Vector3.up) *
                Flatten(transform.forward).normalized;
            PlayRandomTrigger(pauseAnimationTriggers);
        }

        private void ChooseWanderDirection(float maximumTurn)
        {
            desiredDirection = Quaternion.AngleAxis(
                Range(-maximumTurn, maximumTurn), Vector3.up) *
                Flatten(transform.forward).normalized;
            nextHeadingChange = Time.time + Range(headingChangeInterval);
        }

        private void ChooseReturnDirection()
        {
            Vector3 towardSpawn = Flatten(spawnPosition - transform.position);
            if (towardSpawn.sqrMagnitude < 0.001f)
            {
                towardSpawn = -Flatten(transform.forward);
            }

            desiredDirection = Quaternion.AngleAxis(
                Range(-returnAngleVariation, returnAngleVariation), Vector3.up) *
                towardSpawn.normalized;
            returningHome = true;
        }

        private void TurnToward(Vector3 direction, float degreesPerSecond)
        {
            direction = Flatten(direction);
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(
                direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation,
                degreesPerSecond * Time.deltaTime);
        }

        private void ApplyRootMotionWithinSpawnRadius(Vector3 deltaPosition)
        {
            deltaPosition.y = 0f;
            Vector3 currentPosition = GetMovementPosition();
            Vector3 candidate = currentPosition + deltaPosition;
            Vector3 currentOffset = Flatten(currentPosition - spawnPosition);
            float radius = Mathf.Max(0.1f, maximumSpawnDistance);
            if (currentOffset.sqrMagnitude > radius * radius)
            {
                // Pursuit can carry a walker beyond its normal wander radius. Once combat ends,
                // accept only locomotion that brings it closer instead of snapping it home.
                Vector3 candidateOffset = Flatten(candidate - spawnPosition);
                if (candidateOffset.sqrMagnitude < currentOffset.sqrMagnitude)
                {
                    candidate.y = spawnPosition.y;
                    ApplyLocomotionPosition(currentPosition, candidate);
                }
                else
                    StopPhysicsLocomotion();
                return;
            }

            ApplyLocomotionPosition(currentPosition,
                ClampPositionWithinSpawnRadius(candidate));
        }

        private void ApplyPursuitRootMotion(Vector3 deltaPosition)
        {
            deltaPosition.y = 0f;
            Vector3 currentPosition = GetMovementPosition();
            Vector3 candidate = currentPosition + deltaPosition;
            candidate.y = spawnPosition.y;
            ApplyLocomotionPosition(currentPosition, candidate);
        }

        private void ApplyPositionWithinSpawnRadius(Vector3 candidate)
        {
            candidate = ClampPositionWithinSpawnRadius(candidate);
            if (movementBody != null && !movementBody.isKinematic)
            {
                Vector3 bodyPosition = movementBody.position;
                bodyPosition.x = candidate.x;
                bodyPosition.z = candidate.z;
                movementBody.position = bodyPosition;
            }
            else
                transform.position = candidate;
        }

        private Vector3 ClampPositionWithinSpawnRadius(Vector3 candidate)
        {
            Vector3 fromSpawn = Flatten(candidate - spawnPosition);
            float radius = Mathf.Max(0.1f, maximumSpawnDistance);
            if (fromSpawn.sqrMagnitude > radius * radius)
            {
                fromSpawn = fromSpawn.normalized * radius;
                candidate.x = spawnPosition.x + fromSpawn.x;
                candidate.z = spawnPosition.z + fromSpawn.z;
            }
            candidate.y = spawnPosition.y;
            return candidate;
        }

        private Vector3 GetMovementPosition()
        {
            return movementBody != null && !movementBody.isKinematic
                ? movementBody.position
                : transform.position;
        }

        private void ApplyLocomotionPosition(Vector3 currentPosition,
            Vector3 targetPosition)
        {
            if (movementBody == null || movementBody.isKinematic)
            {
                transform.position = targetPosition;
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            Vector3 velocity = (targetPosition - currentPosition) / deltaTime;
            requestedPhysicsVelocity = new Vector3(
                velocity.x, 0f, velocity.z);
            hasPhysicsLocomotionRequest = true;
        }

        private void StopPhysicsLocomotion()
        {
            if (!Application.isPlaying || movementBody == null ||
                movementBody.isKinematic)
                return;

            requestedPhysicsVelocity = Vector3.zero;
            hasPhysicsLocomotionRequest = true;

            // FixedUpdate is not invoked after the component is disabled. Clear
            // the last walking velocity immediately at that transition while
            // preserving gravity and any vertical fall already in progress.
            if (!isActiveAndEnabled)
            {
                Vector3 velocity = movementBody.linearVelocity;
                velocity.x = 0f;
                velocity.z = 0f;
                movementBody.linearVelocity = velocity;
            }
        }

        private void RefreshMovementBody()
        {
            movementBody = GetComponent<Rigidbody>();
            hasPhysicsLocomotionRequest = false;
            nextMovementBodySearch = Time.time + 1f;
        }

        private void RefreshAnimator()
        {
            animator = GetComponent<Animator>();
            nextAnimatorSearch = Time.time + 1f;
            observedController = animator != null
                ? animator.runtimeAnimatorController
                : null;
            hasSpeedParameter = false;
            hasDirectionParameter = false;
            if (animator == null)
            {
                return;
            }

            animator.applyRootMotion = true;

            speedParameterHash = Animator.StringToHash(speedParameter);
            directionParameterHash = Animator.StringToHash(directionParameter);
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.nameHash == speedParameterHash &&
                    parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeedParameter = true;
                }
                if (parameter.nameHash == directionParameterHash &&
                    parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasDirectionParameter = true;
                }
            }
        }

        private void UpdateAnimation(float speed)
        {
            if (animator == null)
            {
                return;
            }

            animator.applyRootMotion = true;
            animator.speed = speed > 0f
                ? Mathf.Max(0.05f, animationPlaybackSpeed)
                : 1f;

            if (hasSpeedParameter)
            {
                animator.SetFloat(speedParameterHash, speed,
                    animationDamping, Time.deltaTime);
            }
            if (hasDirectionParameter)
            {
                float direction = Vector3.SignedAngle(transform.forward,
                    desiredDirection, Vector3.up) / 90f;
                animator.SetFloat(directionParameterHash,
                    Mathf.Clamp(direction, -1f, 1f),
                    animationDamping, Time.deltaTime);
            }
        }

        private void PlayRandomTrigger(string[] triggerNames)
        {
            if (animator == null || triggerNames == null || triggerNames.Length == 0)
            {
                return;
            }

            string triggerName = triggerNames[random.Next(triggerNames.Length)];
            if (string.IsNullOrWhiteSpace(triggerName))
            {
                return;
            }

            int triggerHash = Animator.StringToHash(triggerName);
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == triggerHash &&
                    parameters[i].type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(triggerHash);
                    return;
                }
            }
        }

        private void PlayBumpSound()
        {
            if (bumpSounds == null || bumpSounds.Length == 0)
            {
                return;
            }

            AudioClip clip = bumpSounds[random.Next(bumpSounds.Length)];
            if (clip == null)
            {
                return;
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.playOnAwake = false;
                    audioSource.spatialBlend = 1f;
                }
            }
            audioSource.PlayOneShot(clip, bumpVolume);
        }

        private float DistanceFromSpawnSquared()
        {
            return Flatten(transform.position - spawnPosition).sqrMagnitude;
        }

        private float ReturnReleaseDistanceSquared()
        {
            float releaseDistance = Mathf.Max(0.1f, maximumSpawnDistance) * 0.6f;
            return releaseDistance * releaseDistance;
        }

        private float Range(Vector2 range)
        {
            return Range(range.x, range.y);
        }

        private float Range(float minimum, float maximum)
        {
            if (maximum < minimum)
            {
                (minimum, maximum) = (maximum, minimum);
            }
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static int CreateStableSeed(string objectName, Vector3 position)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string seedText = objectName + "|" +
                    Mathf.RoundToInt(position.x * 100f) + "|" +
                    Mathf.RoundToInt(position.z * 100f);
                for (int i = 0; i < seedText.Length; i++)
                {
                    hash ^= seedText[i];
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        private void OnValidate()
        {
            animationPlaybackSpeed = Mathf.Max(0.05f, animationPlaybackSpeed);
            inPlaceMovementSpeed = Mathf.Max(0f, inPlaceMovementSpeed);
            minimumRootMotionSpeed = Mathf.Max(0f, minimumRootMotionSpeed);
            maximumSpawnDistance = Mathf.Max(0.1f, maximumSpawnDistance);
            turnSpeed = Mathf.Max(1f, turnSpeed);
            personalSpace = Mathf.Max(0.05f, personalSpace);
            lookAheadDistance = Mathf.Max(personalSpace, lookAheadDistance);
            avoidanceCheckInterval = Mathf.Max(0.02f, avoidanceCheckInterval);
            avoidanceCommitDuration = Mathf.Max(0.1f, avoidanceCommitDuration);
            overlapSeparationSpeed = Mathf.Max(0.01f, overlapSeparationSpeed);
            progressCheckInterval = Mathf.Max(0.25f, progressCheckInterval);
            minimumProgressDistance = Mathf.Max(0.001f,
                minimumProgressDistance);
            stalledChecksBeforeRecovery = Mathf.Max(1,
                stalledChecksBeforeRecovery);
            headingNoiseFrequency = Mathf.Max(0.01f, headingNoiseFrequency);
            maximumHealth = Mathf.Max(1, maximumHealth);
            pursuitDuration = Mathf.Max(0.1f, pursuitDuration);
            pursuitStoppingDistance = Mathf.Max(0.1f, pursuitStoppingDistance);
            if (!Application.isPlaying)
                currentHealth = maximumHealth;
        }
    }
}
