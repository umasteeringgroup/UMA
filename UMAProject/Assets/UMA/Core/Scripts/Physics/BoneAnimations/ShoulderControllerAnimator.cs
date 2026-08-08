using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public enum ShoulderControllerSide
    {
        Auto,
        Left,
        Right
    }

    public enum ShoulderEndpointMode
    {
        HandWhenAvailable,
        UpperArmEndpoint
    }

    /// <summary>
    /// Redistributes part of an animated upper-arm pose into the shoulder girdle.
    /// The corresponding arm chain is solved after the shoulder correction so the
    /// selected endpoint stays at the position produced by the Animator.
    /// </summary>
    public class ShoulderControllerAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/Shoulder Controller Animator")]
        public static void CreateAsset()
        {
            UMA.CustomAssetUtility.CreateAsset<ShoulderControllerAnimator>();
        }
#endif

        [Header("Required Bones")]
        [Tooltip("Clavicle/shoulder bone that will receive the procedural correction.")]
        public string ShoulderBoneName = string.Empty;

        [Tooltip("Upper-arm bone driven by the configured shoulder.")]
        public string ArmBoneName = string.Empty;

        [Header("Optional Bone Overrides")]
        [Tooltip("Lower-arm bone. Leave empty to use the Humanoid mapping or hierarchy.")]
        public string LowerArmBoneName = string.Empty;

        [Tooltip("Hand bone. Leave empty to use the Humanoid mapping or hierarchy.")]
        public string HandBoneName = string.Empty;

        [Tooltip("Chest/upper-chest reference. Leave empty to resolve it automatically.")]
        public string TorsoReferenceBoneName = string.Empty;

        [Tooltip("The opposite shoulder can improve torso-frame calibration. Leave empty to resolve it automatically.")]
        public string OppositeShoulderBoneName = string.Empty;

        [Header("Coordinate Space")]
        [Tooltip("Explicitly select a side only when it cannot be inferred from the Humanoid mapping or bone names.")]
        public ShoulderControllerSide Side = ShoulderControllerSide.Auto;

        [Tooltip("Endpoint to keep at the Animator-produced position. Hand mode falls back to the upper-arm endpoint when a complete arm chain is unavailable.")]
        public ShoulderEndpointMode EndpointMode = ShoulderEndpointMode.HandWhenAvailable;

        [Header("Influence")]
        [Range(0f, 1f)]
        public float OverallEffect = 1f;

        [Range(0f, 1f)]
        public float ElevationEffect = 1f;

        [Range(0f, 1f)]
        public float ProtractionEffect = 0.75f;

        [Range(0f, 1f)]
        public float RetractionEffect = 0.5f;

        [Range(0f, 1f)]
        public float PosteriorRollEffect = 0.35f;

        [Header("Shoulder Limits")]
        [Min(0f)]
        public float MaximumElevationDegrees = 18f;

        [Min(0f)]
        public float MaximumProtractionDegrees = 12f;

        [Min(0f)]
        public float MaximumRetractionDegrees = 8f;

        [Min(0f)]
        public float MaximumPosteriorRollDegrees = 8f;

        [Header("Animated Shoulder Constraint")]
        [Tooltip("Prevents the shoulder/clavicle bone from pointing downward after the Animator has written its pose.")]
        public bool PreventShoulderPointingDown = true;

        [Tooltip("Maximum angle, in degrees, that the shoulder may point below the anatomical horizontal plane. Zero keeps it horizontal or above.")]
        [Range(0f, 90f)]
        public float MaximumDownwardShoulderDegrees = 0f;

        [Header("Response Curves")]
        [Tooltip("Input is arm elevation in degrees: 0 is down, 90 is horizontal, and 180 is overhead.")]
        public AnimationCurve ElevationResponse = CreateDefaultElevationCurve();

        [Tooltip("Input is the normalized forward component of the upper-arm direction.")]
        public AnimationCurve ProtractionResponse = CreateDefaultUnitCurve();

        [Tooltip("Input is the normalized backward component of the upper-arm direction.")]
        public AnimationCurve RetractionResponse = CreateDefaultUnitCurve();

        [Tooltip("Input is arm elevation in degrees. The default keeps this channel disabled at and below horizontal.")]
        public AnimationCurve PosteriorRollResponse = CreateDefaultPosteriorRollCurve();

        [Header("Solve")]
        [Tooltip("Maximum accepted world-space endpoint error. Shoulder influence is reduced when necessary to remain within this tolerance.")]
        [Min(0.000001f)]
        public float EndpointTolerance = 0.0005f;

        [Tooltip("Optional half-life for smoothing the procedural shoulder delta. Zero disables temporal damping.")]
        [Min(0f)]
        public float DampingHalfLife = 0f;

        [Tooltip("Restore the hand's Animator-produced world rotation after solving the chain.")]
        public bool PreserveHandRotation = true;

        [Tooltip("Write coordinate-space and solve diagnostics to the Console.")]
        public bool DebugMode;

        public override void Initialize(UMAData umaData, SlotData slotData)
        {
            base.Initialize(umaData, slotData);
            ValidateSettings();

            if (umaData == null)
            {
                return;
            }

            ShoulderControllerRuntime runtime =
                umaData.GetComponent<ShoulderControllerRuntime>();
            if (runtime == null)
            {
                runtime = umaData.gameObject.AddComponent<ShoulderControllerRuntime>();
            }

            runtime.RegisterOrUpdate(umaData, this);

            // This asset is intentionally inert in BaseUpdatedObject.FixedUpdate.
            // Runtime registration success is character-specific, while this
            // ScriptableObject can be shared by many characters.
            initialized = true;
        }

        public override void DoUpdate(UMAData data, float step)
        {
            // ShoulderControllerRuntime evaluates after the Animator in LateUpdate.
        }

        public void ValidateSettings()
        {
            OverallEffect = Mathf.Clamp01(OverallEffect);
            ElevationEffect = Mathf.Clamp01(ElevationEffect);
            ProtractionEffect = Mathf.Clamp01(ProtractionEffect);
            RetractionEffect = Mathf.Clamp01(RetractionEffect);
            PosteriorRollEffect = Mathf.Clamp01(PosteriorRollEffect);

            MaximumElevationDegrees = Mathf.Max(0f, MaximumElevationDegrees);
            MaximumProtractionDegrees = Mathf.Max(0f, MaximumProtractionDegrees);
            MaximumRetractionDegrees = Mathf.Max(0f, MaximumRetractionDegrees);
            MaximumPosteriorRollDegrees = Mathf.Max(0f, MaximumPosteriorRollDegrees);
            MaximumDownwardShoulderDegrees = Mathf.Clamp(
                MaximumDownwardShoulderDegrees,
                0f,
                90f);
            EndpointTolerance = Mathf.Max(0.000001f, EndpointTolerance);
            DampingHalfLife = Mathf.Max(0f, DampingHalfLife);

            if (ElevationResponse == null || ElevationResponse.length == 0)
            {
                ElevationResponse = CreateDefaultElevationCurve();
            }

            if (ProtractionResponse == null || ProtractionResponse.length == 0)
            {
                ProtractionResponse = CreateDefaultUnitCurve();
            }

            if (RetractionResponse == null || RetractionResponse.length == 0)
            {
                RetractionResponse = CreateDefaultUnitCurve();
            }

            if (PosteriorRollResponse == null || PosteriorRollResponse.length == 0)
            {
                PosteriorRollResponse = CreateDefaultPosteriorRollCurve();
            }
        }

        public void ResetResponseCurves()
        {
            ElevationResponse = CreateDefaultElevationCurve();
            ProtractionResponse = CreateDefaultUnitCurve();
            RetractionResponse = CreateDefaultUnitCurve();
            PosteriorRollResponse = CreateDefaultPosteriorRollCurve();
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        private static AnimationCurve CreateDefaultElevationCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(25f, 0f),
                new Keyframe(90f, 0.45f),
                new Keyframe(150f, 0.9f),
                new Keyframe(180f, 1f));
        }

        private static AnimationCurve CreateDefaultUnitCurve()
        {
            return AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        private static AnimationCurve CreateDefaultPosteriorRollCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(90f, 0f),
                new Keyframe(145f, 0.8f),
                new Keyframe(180f, 1f));
        }
    }

    /// <summary>
    /// Per-character runtime for ShoulderControllerAnimator assets.
    ///
    /// All direction analysis and endpoint solving uses the final runtime
    /// transforms. RaceData.FixupRotations is deliberately not read here: Root,
    /// Global, Position, external roots, DNA, and imported bone orientation are
    /// already represented by those transforms.
    /// </summary>
    [DefaultExecutionOrder(9000)]
    [DisallowMultipleComponent]
    public sealed class ShoulderControllerRuntime : MonoBehaviour
    {
        public struct AnatomicalFrame
        {
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Forward;
            public Vector3 Outward;
            public ShoulderControllerSide Side;
            public bool Reflected;
        }

        private sealed class Registration
        {
            public ShoulderControllerAnimator Asset;
            public UMAData Data;
            public Animator Animator;
            public Transform CharacterRoot;
            public Transform ReferenceRoot;
            public Transform Torso;
            public Transform OppositeShoulder;
            public Transform Hips;
            public Transform Head;
            public Transform Shoulder;
            public Transform Arm;
            public Transform LowerArm;
            public Transform Hand;
            public ShoulderControllerSide ResolvedSide;

            public bool HasPreviousOutput;
            public Quaternion LastSourceShoulderLocal;
            public Quaternion LastSourceArmLocal;
            public Quaternion LastSourceLowerArmLocal;
            public Quaternion LastSourceHandLocal;
            public Quaternion LastAppliedShoulderLocal;
            public Quaternion LastAppliedArmLocal;
            public Quaternion LastAppliedLowerArmLocal;
            public Quaternion LastAppliedHandLocal;

            public Quaternion SmoothedShoulderDelta = Quaternion.identity;
            public Vector3 CachedBendDirection;
            public bool WasReflected;
        }

        private readonly Dictionary<int, Registration> _registrations =
            new Dictionary<int, Registration>();

        private UMAData _umaData;
        private bool _subscribed;

        [Tooltip("Disable this when a custom animation pipeline calls EvaluateNow explicitly.")]
        public bool AutomaticUpdate = true;

        public int RegistrationCount
        {
            get { return _registrations.Count; }
        }

        public bool HasReflectedRegistration
        {
            get
            {
                foreach (Registration registration in _registrations.Values)
                {
                    if (registration.WasReflected)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void Awake()
        {
            _umaData = GetComponent<UMAData>();
            Subscribe();
        }

        private void OnEnable()
        {
            if (_umaData == null)
            {
                _umaData = GetComponent<UMAData>();
            }

            Subscribe();
        }

        private void OnDisable()
        {
            RestoreAllUnwrittenPoses();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (AutomaticUpdate)
            {
                EvaluateNow();
            }
        }

        public bool RegisterOrUpdate(
            UMAData data,
            ShoulderControllerAnimator asset)
        {
            if (data == null || asset == null)
            {
                return false;
            }

            asset.ValidateSettings();
            _umaData = data;
            Subscribe();

            int key = asset.GetInstanceID();
            Registration registration;
            if (!_registrations.TryGetValue(key, out registration))
            {
                registration = new Registration();
                _registrations.Add(key, registration);
            }
            else
            {
                RestoreUnwrittenPose(registration);
            }

            if (!TryResolveRegistration(data, asset, registration))
            {
                _registrations.Remove(key);
                return false;
            }

            registration.HasPreviousOutput = false;
            registration.SmoothedShoulderDelta = Quaternion.identity;
            registration.CachedBendDirection = Vector3.zero;

            if (asset.DebugMode)
            {
                Debug.Log(
                    "Shoulder Controller registered '" + asset.name +
                    "' on '" + data.name + "' using " +
                    registration.ResolvedSide + " anatomical space" +
                    (registration.WasReflected
                        ? " with reflected ancestry detected."
                        : "."),
                    data);
            }

            return true;
        }

        /// <summary>
        /// Evaluates every registered shoulder immediately. Custom procedural
        /// animation pipelines can disable AutomaticUpdate and call this after
        /// their own IK pass.
        /// </summary>
        public void EvaluateNow()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_umaData != null && !_umaData.BoneAnimatorsEnabled)
            {
                RestoreAllUnwrittenPoses();
                return;
            }

            foreach (Registration registration in _registrations.Values)
            {
                EvaluateRegistration(registration);
            }
        }

        private void EvaluateRegistration(Registration registration)
        {
            ShoulderControllerAnimator asset = registration.Asset;
            if (asset == null ||
                registration.Shoulder == null ||
                registration.Arm == null ||
                registration.LowerArm == null)
            {
                return;
            }

            if (registration.Animator != null && !registration.Animator.enabled)
            {
                RestoreUnwrittenPose(registration);
                return;
            }

            RestoreUnwrittenPose(registration);

            Quaternion sourceShoulderLocal = registration.Shoulder.localRotation;
            Quaternion sourceArmLocal = registration.Arm.localRotation;
            Quaternion sourceLowerArmLocal = registration.LowerArm.localRotation;
            Quaternion sourceHandLocal = registration.Hand != null
                ? registration.Hand.localRotation
                : Quaternion.identity;

            Quaternion sourceShoulderWorld = registration.Shoulder.rotation;
            Quaternion sourceLowerArmWorld = registration.LowerArm.rotation;
            Quaternion sourceHandWorld = registration.Hand != null
                ? registration.Hand.rotation
                : Quaternion.identity;

            Vector3 sourceShoulderPosition = registration.Shoulder.position;
            Vector3 sourceArmPosition = registration.Arm.position;
            Vector3 sourceElbowPosition = registration.LowerArm.position;
            Vector3 sourceHandPosition = registration.Hand != null
                ? registration.Hand.position
                : sourceElbowPosition;

            AnatomicalFrame frame;
            if (!TryBuildAnatomicalFrame(registration, out frame))
            {
                RecordOutput(
                    registration,
                    sourceShoulderLocal,
                    sourceArmLocal,
                    sourceLowerArmLocal,
                    sourceHandLocal);
                return;
            }

            registration.WasReflected = frame.Reflected;

            Vector3 armDirection = sourceElbowPosition - sourceArmPosition;
            if (!TryNormalize(ref armDirection))
            {
                RecordOutput(
                    registration,
                    sourceShoulderLocal,
                    sourceArmLocal,
                    sourceLowerArmLocal,
                    sourceHandLocal);
                return;
            }

            float elevationDegrees = Vector3.Angle(-frame.Up, armDirection);
            float forwardComponent = Mathf.Clamp01(
                Vector3.Dot(armDirection, frame.Forward));
            float backwardComponent = Mathf.Clamp01(
                -Vector3.Dot(armDirection, frame.Forward));

            float overall = Mathf.Clamp01(asset.OverallEffect);
            float elevation = asset.MaximumElevationDegrees *
                              asset.ElevationEffect *
                              EvaluateCurve(asset.ElevationResponse, elevationDegrees) *
                              overall;
            float protraction = asset.MaximumProtractionDegrees *
                                asset.ProtractionEffect *
                                EvaluateCurve(asset.ProtractionResponse, forwardComponent) *
                                overall;
            float retraction = asset.MaximumRetractionDegrees *
                               asset.RetractionEffect *
                               EvaluateCurve(asset.RetractionResponse, backwardComponent) *
                               overall;
            float posteriorRoll = asset.MaximumPosteriorRollDegrees *
                                  asset.PosteriorRollEffect *
                                  EvaluateCurve(asset.PosteriorRollResponse, elevationDegrees) *
                                  overall;

            float sideSign = frame.Side == ShoulderControllerSide.Right
                ? 1f
                : -1f;

            Quaternion elevationDelta = Quaternion.AngleAxis(
                elevation * sideSign,
                frame.Forward);
            Quaternion reachDelta = Quaternion.AngleAxis(
                (retraction - protraction) * sideSign,
                frame.Up);
            Quaternion rollDelta = Quaternion.AngleAxis(
                posteriorRoll,
                frame.Outward);
            Quaternion targetDelta = rollDelta * reachDelta * elevationDelta;

            if (asset.DampingHalfLife > 0f && Application.isPlaying)
            {
                float interpolation = 1f -
                    Mathf.Pow(
                        0.5f,
                        Mathf.Max(0f, Time.deltaTime) /
                        Mathf.Max(0.000001f, asset.DampingHalfLife));
                registration.SmoothedShoulderDelta = Quaternion.Slerp(
                    registration.SmoothedShoulderDelta,
                    targetDelta,
                    interpolation);
            }
            else
            {
                registration.SmoothedShoulderDelta = targetDelta;
            }

            bool solveHand =
                asset.EndpointMode == ShoulderEndpointMode.HandWhenAvailable &&
                registration.Hand != null &&
                registration.Hand.IsChildOf(registration.LowerArm);

            float upperLength = Vector3.Distance(
                sourceArmPosition,
                sourceElbowPosition);
            float lowerLength = solveHand
                ? Vector3.Distance(sourceElbowPosition, sourceHandPosition)
                : 0f;

            if (upperLength <= 0.000001f ||
                (solveHand && lowerLength <= 0.000001f))
            {
                RecordOutput(
                    registration,
                    sourceShoulderLocal,
                    sourceArmLocal,
                    sourceLowerArmLocal,
                    sourceHandLocal);
                return;
            }

            Quaternion desiredDelta = registration.SmoothedShoulderDelta;
            float influence = solveHand
                ? FindMaximumTwoBoneInfluence(
                    sourceShoulderPosition,
                    sourceArmPosition,
                    sourceHandPosition,
                    upperLength,
                    lowerLength,
                    desiredDelta,
                    asset.EndpointTolerance)
                : FindMaximumSingleBoneInfluence(
                    sourceShoulderPosition,
                    sourceArmPosition,
                    sourceElbowPosition,
                    upperLength,
                    desiredDelta,
                    asset.EndpointTolerance);

            Quaternion appliedDelta = Quaternion.Slerp(
                Quaternion.identity,
                desiredDelta,
                influence);
            SetWorldRotation(
                registration.Shoulder,
                appliedDelta * sourceShoulderWorld);

            bool downwardShoulderSuppressed = false;
            if (asset.PreventShoulderPointingDown)
            {
                Vector3 shoulderDirection =
                    registration.Arm.position -
                    registration.Shoulder.position;
                Quaternion downwardLimitCorrection;
                if (TryCalculateDownwardLimitCorrection(
                        shoulderDirection,
                        frame.Up,
                        frame.Outward,
                        asset.MaximumDownwardShoulderDegrees,
                        out downwardLimitCorrection))
                {
                    SetWorldRotation(
                        registration.Shoulder,
                        downwardLimitCorrection *
                        registration.Shoulder.rotation);
                    downwardShoulderSuppressed = true;
                }
            }

            Quaternion constrainedShoulderWorld =
                registration.Shoulder.rotation;

            bool solved;
            if (solveHand)
            {
                solved = SolveTwoBoneChain(
                    registration,
                    frame,
                    sourceElbowPosition,
                    sourceHandPosition,
                    sourceHandWorld,
                    upperLength,
                    lowerLength,
                    asset.PreserveHandRotation);
            }
            else
            {
                solved = SolveUpperArmEndpoint(
                    registration,
                    sourceElbowPosition,
                    sourceLowerArmWorld,
                    sourceHandWorld,
                    asset.PreserveHandRotation);
            }

            if (!solved)
            {
                SetWorldRotation(
                    registration.Shoulder,
                    downwardShoulderSuppressed
                        ? constrainedShoulderWorld
                        : sourceShoulderWorld);
                SetWorldRotation(
                    registration.Arm,
                    registration.Arm.parent != null
                        ? registration.Arm.parent.rotation * sourceArmLocal
                        : sourceArmLocal);
                SetWorldRotation(
                    registration.LowerArm,
                    sourceLowerArmWorld);
                if (registration.Hand != null)
                {
                    SetWorldRotation(registration.Hand, sourceHandWorld);
                }
            }

            if (asset.DebugMode)
            {
                Vector3 endpoint = solveHand && registration.Hand != null
                    ? registration.Hand.position
                    : registration.LowerArm.position;
                Vector3 target = solveHand
                    ? sourceHandPosition
                    : sourceElbowPosition;
                float error = Vector3.Distance(endpoint, target);
                if (error > asset.EndpointTolerance * 2f)
                {
                    Debug.LogWarning(
                        "Shoulder Controller endpoint error on '" +
                        registration.Data.name + "' is " +
                        error.ToString("G6") + " (influence " +
                        influence.ToString("F3") + ").",
                        registration.Data);
                }
            }

            RecordOutput(
                registration,
                sourceShoulderLocal,
                sourceArmLocal,
                sourceLowerArmLocal,
                sourceHandLocal);
        }

        /// <summary>
        /// Calculates the world-space rotation needed to keep a bone direction
        /// from dropping farther than the configured angle below horizontal.
        /// </summary>
        public static bool TryCalculateDownwardLimitCorrection(
            Vector3 direction,
            Vector3 anatomicalUp,
            Vector3 fallbackHorizontal,
            float maximumDownwardDegrees,
            out Quaternion correction)
        {
            correction = Quaternion.identity;
            if (!TryNormalize(ref direction) ||
                !TryNormalize(ref anatomicalUp))
            {
                return false;
            }

            float clampedMaximum = Mathf.Clamp(
                maximumDownwardDegrees,
                0f,
                90f);
            float maximumRadians = clampedMaximum * Mathf.Deg2Rad;
            float minimumUpComponent = -Mathf.Sin(maximumRadians);
            if (Vector3.Dot(direction, anatomicalUp) >=
                minimumUpComponent - 0.000001f)
            {
                return false;
            }

            Vector3 horizontalDirection = Vector3.ProjectOnPlane(
                direction,
                anatomicalUp);
            if (!TryNormalize(ref horizontalDirection))
            {
                horizontalDirection = Vector3.ProjectOnPlane(
                    fallbackHorizontal,
                    anatomicalUp);
                if (!TryNormalize(ref horizontalDirection))
                {
                    return false;
                }
            }

            Vector3 limitedDirection =
                horizontalDirection * Mathf.Cos(maximumRadians) -
                anatomicalUp * Mathf.Sin(maximumRadians);
            if (!TryNormalize(ref limitedDirection))
            {
                return false;
            }

            correction = Quaternion.FromToRotation(
                direction,
                limitedDirection);
            return true;
        }

        private static bool SolveTwoBoneChain(
            Registration registration,
            AnatomicalFrame frame,
            Vector3 animatedElbowPosition,
            Vector3 targetHandPosition,
            Quaternion targetHandRotation,
            float upperLength,
            float lowerLength,
            bool preserveHandRotation)
        {
            Vector3 rootPosition = registration.Arm.position;
            Vector3 fallbackBend = registration.CachedBendDirection;
            if (!TryNormalize(ref fallbackBend))
            {
                fallbackBend = -frame.Forward;
            }

            Vector3 solvedElbow;
            Vector3 bendDirection;
            if (!TryCalculateTwoBoneElbow(
                    rootPosition,
                    targetHandPosition,
                    animatedElbowPosition,
                    fallbackBend,
                    upperLength,
                    lowerLength,
                    out solvedElbow,
                    out bendDirection))
            {
                return false;
            }

            registration.CachedBendDirection = bendDirection;

            Vector3 currentUpperDirection =
                registration.LowerArm.position - registration.Arm.position;
            Vector3 desiredUpperDirection =
                solvedElbow - registration.Arm.position;
            if (!TryNormalize(ref currentUpperDirection) ||
                !TryNormalize(ref desiredUpperDirection))
            {
                return false;
            }

            Quaternion upperCorrection = Quaternion.FromToRotation(
                currentUpperDirection,
                desiredUpperDirection);
            SetWorldRotation(
                registration.Arm,
                upperCorrection * registration.Arm.rotation);

            Vector3 currentLowerDirection =
                registration.Hand.position - registration.LowerArm.position;
            Vector3 desiredLowerDirection =
                targetHandPosition - registration.LowerArm.position;
            if (!TryNormalize(ref currentLowerDirection) ||
                !TryNormalize(ref desiredLowerDirection))
            {
                return false;
            }

            Quaternion lowerCorrection = Quaternion.FromToRotation(
                currentLowerDirection,
                desiredLowerDirection);
            SetWorldRotation(
                registration.LowerArm,
                lowerCorrection * registration.LowerArm.rotation);

            if (preserveHandRotation)
            {
                SetWorldRotation(registration.Hand, targetHandRotation);
            }

            return true;
        }

        private static bool SolveUpperArmEndpoint(
            Registration registration,
            Vector3 targetElbowPosition,
            Quaternion targetLowerArmRotation,
            Quaternion targetHandRotation,
            bool preserveHandRotation)
        {
            Vector3 currentDirection =
                registration.LowerArm.position - registration.Arm.position;
            Vector3 desiredDirection =
                targetElbowPosition - registration.Arm.position;
            if (!TryNormalize(ref currentDirection) ||
                !TryNormalize(ref desiredDirection))
            {
                return false;
            }

            Quaternion correction = Quaternion.FromToRotation(
                currentDirection,
                desiredDirection);
            SetWorldRotation(
                registration.Arm,
                correction * registration.Arm.rotation);

            // Keep the downstream animation orientation after moving its pivot.
            SetWorldRotation(registration.LowerArm, targetLowerArmRotation);
            if (preserveHandRotation && registration.Hand != null)
            {
                SetWorldRotation(registration.Hand, targetHandRotation);
            }

            return true;
        }

        public static bool TryCalculateTwoBoneElbow(
            Vector3 rootPosition,
            Vector3 targetPosition,
            Vector3 polePosition,
            Vector3 fallbackBendDirection,
            float upperLength,
            float lowerLength,
            out Vector3 elbowPosition,
            out Vector3 bendDirection)
        {
            elbowPosition = rootPosition;
            bendDirection = Vector3.zero;

            if (upperLength <= 0.000001f || lowerLength <= 0.000001f)
            {
                return false;
            }

            Vector3 targetVector = targetPosition - rootPosition;
            float targetDistance = targetVector.magnitude;
            if (targetDistance <= 0.000001f)
            {
                return false;
            }

            Vector3 targetDirection = targetVector / targetDistance;
            float minimumReach = Mathf.Abs(upperLength - lowerLength);
            float maximumReach = upperLength + lowerLength;
            float clampedDistance = Mathf.Clamp(
                targetDistance,
                minimumReach + 0.000001f,
                maximumReach - 0.000001f);

            Vector3 poleVector = polePosition - rootPosition;
            bendDirection = Vector3.ProjectOnPlane(
                poleVector,
                targetDirection);
            if (!TryNormalize(ref bendDirection))
            {
                bendDirection = Vector3.ProjectOnPlane(
                    fallbackBendDirection,
                    targetDirection);
                if (!TryNormalize(ref bendDirection))
                {
                    Vector3 fallbackAxis =
                        Mathf.Abs(Vector3.Dot(targetDirection, Vector3.up)) <
                        0.95f
                            ? Vector3.up
                            : Vector3.forward;
                    bendDirection = Vector3.Cross(
                        targetDirection,
                        fallbackAxis);
                    if (!TryNormalize(ref bendDirection))
                    {
                        return false;
                    }
                }
            }

            float along =
                (clampedDistance * clampedDistance +
                 upperLength * upperLength -
                 lowerLength * lowerLength) /
                (2f * clampedDistance);
            float heightSquared = Mathf.Max(
                0f,
                upperLength * upperLength - along * along);
            float height = Mathf.Sqrt(heightSquared);

            elbowPosition =
                rootPosition +
                targetDirection * along +
                bendDirection * height;
            return IsFinite(elbowPosition);
        }

        public static float FindMaximumTwoBoneInfluence(
            Vector3 shoulderPosition,
            Vector3 armPosition,
            Vector3 targetPosition,
            float upperLength,
            float lowerLength,
            Quaternion desiredShoulderDelta,
            float tolerance)
        {
            return FindMaximumInfluence(
                delegate(float influence)
                {
                    Vector3 candidateArmPosition =
                        RotatePointAroundPivot(
                            armPosition,
                            shoulderPosition,
                            Quaternion.Slerp(
                                Quaternion.identity,
                                desiredShoulderDelta,
                                influence));
                    float distance = Vector3.Distance(
                        candidateArmPosition,
                        targetPosition);
                    return
                        distance <= upperLength + lowerLength + tolerance &&
                        distance >=
                        Mathf.Abs(upperLength - lowerLength) - tolerance;
                });
        }

        public static float FindMaximumSingleBoneInfluence(
            Vector3 shoulderPosition,
            Vector3 armPosition,
            Vector3 targetPosition,
            float armLength,
            Quaternion desiredShoulderDelta,
            float tolerance)
        {
            return FindMaximumInfluence(
                delegate(float influence)
                {
                    Vector3 candidateArmPosition =
                        RotatePointAroundPivot(
                            armPosition,
                            shoulderPosition,
                            Quaternion.Slerp(
                                Quaternion.identity,
                                desiredShoulderDelta,
                                influence));
                    float distance = Vector3.Distance(
                        candidateArmPosition,
                        targetPosition);
                    return Mathf.Abs(distance - armLength) <= tolerance;
                });
        }

        private static float FindMaximumInfluence(
            Func<float, bool> isValid)
        {
            if (isValid(1f))
            {
                return 1f;
            }

            float low = 0f;
            float high = 1f;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                float midpoint = (low + high) * 0.5f;
                if (isValid(midpoint))
                {
                    low = midpoint;
                }
                else
                {
                    high = midpoint;
                }
            }

            return low;
        }

        private bool TryResolveRegistration(
            UMAData data,
            ShoulderControllerAnimator asset,
            Registration registration)
        {
            if (data.skeleton == null)
            {
                Debug.LogWarning(
                    "Shoulder Controller cannot initialize because '" +
                    data.name + "' has no UMA skeleton.",
                    data);
                return false;
            }

            Transform shoulder = ResolveNamedBone(
                data,
                asset.ShoulderBoneName);
            Transform arm = ResolveNamedBone(data, asset.ArmBoneName);
            if (shoulder == null || arm == null)
            {
                Debug.LogWarning(
                    "Shoulder Controller '" + asset.name +
                    "' requires valid Shoulder and Arm bone names on '" +
                    data.name + "'.",
                    data);
                return false;
            }

            if (!arm.IsChildOf(shoulder))
            {
                Debug.LogWarning(
                    "Shoulder Controller arm '" + arm.name +
                    "' is not a descendant of shoulder '" +
                    shoulder.name + "'.",
                    data);
                return false;
            }

            Animator animator = data.animator != null
                ? data.animator
                : data.GetComponent<Animator>();
            ShoulderControllerSide side = ResolveSide(
                asset.Side,
                animator,
                shoulder,
                arm,
                data.transform);

            Transform lowerArm = ResolveNamedBone(
                data,
                asset.LowerArmBoneName);
            if (lowerArm == null)
            {
                lowerArm = ResolveHumanoidBone(
                    animator,
                    side == ShoulderControllerSide.Right
                        ? HumanBodyBones.RightLowerArm
                        : HumanBodyBones.LeftLowerArm);
            }
            if (lowerArm == null || !lowerArm.IsChildOf(arm))
            {
                lowerArm = FindLikelyDescendant(
                    arm,
                    "lowerarm",
                    "forearm",
                    "elbow");
            }

            if (lowerArm == null || !lowerArm.IsChildOf(arm))
            {
                Debug.LogWarning(
                    "Shoulder Controller could not resolve an upper-arm " +
                    "endpoint/lower-arm descendant for '" + arm.name + "'.",
                    data);
                return false;
            }

            Transform hand = ResolveNamedBone(data, asset.HandBoneName);
            if (hand == null)
            {
                hand = ResolveHumanoidBone(
                    animator,
                    side == ShoulderControllerSide.Right
                        ? HumanBodyBones.RightHand
                        : HumanBodyBones.LeftHand);
            }
            if (hand == null || !hand.IsChildOf(lowerArm))
            {
                hand = FindLikelyDescendant(
                    lowerArm,
                    "hand",
                    "wrist");
            }
            if (hand != null && !hand.IsChildOf(lowerArm))
            {
                hand = null;
            }

            Transform torso = ResolveNamedBone(
                data,
                asset.TorsoReferenceBoneName);
            if (torso == null)
            {
                torso =
                    ResolveHumanoidBone(animator, HumanBodyBones.UpperChest) ??
                    ResolveHumanoidBone(animator, HumanBodyBones.Chest) ??
                    ResolveHumanoidBone(animator, HumanBodyBones.Spine) ??
                    shoulder.parent;
            }

            Transform oppositeShoulder = ResolveNamedBone(
                data,
                asset.OppositeShoulderBoneName);
            if (oppositeShoulder == null)
            {
                HumanBodyBones oppositeShoulderBone =
                    side == ShoulderControllerSide.Right
                        ? HumanBodyBones.LeftShoulder
                        : HumanBodyBones.RightShoulder;
                oppositeShoulder = ResolveHumanoidBone(
                    animator,
                    oppositeShoulderBone);
                if (oppositeShoulder == null)
                {
                    oppositeShoulder = ResolveHumanoidBone(
                        animator,
                        side == ShoulderControllerSide.Right
                            ? HumanBodyBones.LeftUpperArm
                            : HumanBodyBones.RightUpperArm);
                }
            }

            registration.Asset = asset;
            registration.Data = data;
            registration.Animator = animator;
            registration.CharacterRoot = data.transform;
            registration.ReferenceRoot =
                data.GetGlobalTransform() ?? data.transform;
            registration.Torso = torso != null ? torso : shoulder.parent;
            registration.OppositeShoulder = oppositeShoulder;
            registration.Hips = ResolveHumanoidBone(
                animator,
                HumanBodyBones.Hips);
            registration.Head =
                ResolveHumanoidBone(animator, HumanBodyBones.Head) ??
                ResolveHumanoidBone(animator, HumanBodyBones.Neck);
            registration.Shoulder = shoulder;
            registration.Arm = arm;
            registration.LowerArm = lowerArm;
            registration.Hand = hand;
            registration.ResolvedSide = side;
            registration.WasReflected =
                IsReflected(shoulder.localToWorldMatrix) ||
                IsReflected(arm.localToWorldMatrix);
            return true;
        }

        private static bool TryBuildAnatomicalFrame(
            Registration registration,
            out AnatomicalFrame frame)
        {
            frame = new AnatomicalFrame
            {
                Side = registration.ResolvedSide,
                Reflected =
                    IsReflected(registration.Shoulder.localToWorldMatrix) ||
                    IsReflected(registration.Arm.localToWorldMatrix)
            };

            Vector3 up = Vector3.zero;
            if (registration.Hips != null && registration.Head != null)
            {
                up = registration.Head.position - registration.Hips.position;
            }

            if (!TryNormalize(ref up) &&
                registration.Torso != null &&
                registration.Torso.parent != null)
            {
                up =
                    registration.Torso.position -
                    registration.Torso.parent.position;
            }

            if (!TryNormalize(ref up))
            {
                up = registration.CharacterRoot != null
                    ? registration.CharacterRoot.up
                    : Vector3.up;
            }

            if (!TryNormalize(ref up))
            {
                return false;
            }

            Vector3 right = Vector3.zero;
            if (registration.OppositeShoulder != null)
            {
                right = registration.ResolvedSide ==
                        ShoulderControllerSide.Right
                    ? registration.Shoulder.position -
                      registration.OppositeShoulder.position
                    : registration.OppositeShoulder.position -
                      registration.Shoulder.position;
            }

            if (!TryNormalize(ref right))
            {
                Vector3 torsoPosition = registration.Torso != null
                    ? registration.Torso.position
                    : registration.Shoulder.parent != null
                        ? registration.Shoulder.parent.position
                        : registration.Shoulder.position;
                Vector3 lateralOutward =
                    registration.Shoulder.position - torsoPosition;
                if (!TryNormalize(ref lateralOutward))
                {
                    lateralOutward =
                        registration.Arm.position -
                        registration.Shoulder.position;
                }

                if (!TryNormalize(ref lateralOutward))
                {
                    return false;
                }

                right = registration.ResolvedSide ==
                        ShoulderControllerSide.Right
                    ? lateralOutward
                    : -lateralOutward;
            }

            right = Vector3.ProjectOnPlane(right, up);
            if (!TryNormalize(ref right))
            {
                Vector3 fallbackRight = registration.CharacterRoot != null
                    ? registration.CharacterRoot.right
                    : Vector3.right;
                right = Vector3.ProjectOnPlane(fallbackRight, up);
                if (!TryNormalize(ref right))
                {
                    return false;
                }
            }

            Vector3 forward = Vector3.Cross(right, up);
            if (!TryNormalize(ref forward))
            {
                return false;
            }

            // Re-orthogonalize so rotations never inherit skew or reflection
            // from a negative-scale ancestor matrix.
            right = Vector3.Cross(up, forward);
            if (!TryNormalize(ref right))
            {
                return false;
            }

            Vector3 outward =
                registration.ResolvedSide == ShoulderControllerSide.Right
                    ? right
                    : -right;

            frame.Right = right;
            frame.Up = up;
            frame.Forward = forward;
            frame.Outward = outward;
            return true;
        }

        public static bool TryBuildAnatomicalFrame(
            Vector3 upDirection,
            Vector3 rightDirection,
            ShoulderControllerSide side,
            bool reflected,
            out AnatomicalFrame frame)
        {
            frame = new AnatomicalFrame
            {
                Side = side,
                Reflected = reflected
            };

            if (!TryNormalize(ref upDirection))
            {
                return false;
            }

            rightDirection = Vector3.ProjectOnPlane(
                rightDirection,
                upDirection);
            if (!TryNormalize(ref rightDirection))
            {
                return false;
            }

            Vector3 forward = Vector3.Cross(
                rightDirection,
                upDirection);
            if (!TryNormalize(ref forward))
            {
                return false;
            }

            rightDirection = Vector3.Cross(upDirection, forward);
            if (!TryNormalize(ref rightDirection))
            {
                return false;
            }

            frame.Right = rightDirection;
            frame.Up = upDirection;
            frame.Forward = forward;
            frame.Outward = side == ShoulderControllerSide.Right
                ? rightDirection
                : -rightDirection;
            return true;
        }

        public static bool IsReflected(Matrix4x4 matrix)
        {
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);
            return Vector3.Dot(Vector3.Cross(x, y), z) < 0f;
        }

        private static ShoulderControllerSide ResolveSide(
            ShoulderControllerSide configuredSide,
            Animator animator,
            Transform shoulder,
            Transform arm,
            Transform characterRoot)
        {
            if (configuredSide != ShoulderControllerSide.Auto)
            {
                return configuredSide;
            }

            if (ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.RightUpperArm) == arm ||
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.RightShoulder) == shoulder)
            {
                return ShoulderControllerSide.Right;
            }

            if (ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.LeftUpperArm) == arm ||
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.LeftShoulder) == shoulder)
            {
                return ShoulderControllerSide.Left;
            }

            string combinedName =
                (shoulder.name + " " + arm.name).ToLowerInvariant();
            if (ContainsSideToken(combinedName, "right", "r_"))
            {
                return ShoulderControllerSide.Right;
            }

            if (ContainsSideToken(combinedName, "left", "l_"))
            {
                return ShoulderControllerSide.Left;
            }

            Vector3 origin = shoulder.parent != null
                ? shoulder.parent.position
                : characterRoot.position;
            Vector3 lateral = shoulder.position - origin;
            float side = Vector3.Dot(lateral, characterRoot.right);
            return side >= 0f
                ? ShoulderControllerSide.Right
                : ShoulderControllerSide.Left;
        }

        private static bool ContainsSideToken(
            string value,
            string word,
            string prefix)
        {
            return value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   value.IndexOf(
                       " " + prefix,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Transform ResolveNamedBone(
            UMAData data,
            string boneName)
        {
            if (data == null ||
                data.skeleton == null ||
                string.IsNullOrWhiteSpace(boneName))
            {
                return null;
            }

            return data.skeleton.GetBoneTransform(
                UMAUtils.StringToHash(boneName.Trim()));
        }

        private static Transform ResolveHumanoidBone(
            Animator animator,
            HumanBodyBones bone)
        {
            if (animator == null ||
                animator.avatar == null ||
                !animator.avatar.isHuman)
            {
                return null;
            }

            return animator.GetBoneTransform(bone);
        }

        private static Transform FindLikelyDescendant(
            Transform root,
            params string[] tokens)
        {
            if (root == null)
            {
                return null;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(
                true);
            for (int tokenIndex = 0;
                 tokenIndex < tokens.Length;
                 tokenIndex++)
            {
                string token = tokens[tokenIndex];
                for (int index = 0; index < descendants.Length; index++)
                {
                    Transform candidate = descendants[index];
                    if (candidate == root)
                    {
                        continue;
                    }

                    if (candidate.name.IndexOf(
                            token,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate;
                    }
                }
            }

            return root.childCount > 0 ? root.GetChild(0) : null;
        }

        private void Subscribe()
        {
            if (_subscribed || _umaData == null)
            {
                return;
            }

            _umaData.OnCharacterBegun += OnCharacterBegun;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _umaData == null)
            {
                return;
            }

            _umaData.OnCharacterBegun -= OnCharacterBegun;
            _subscribed = false;
        }

        private void OnCharacterBegun(UMAData data)
        {
            RestoreAllUnwrittenPoses();
            _registrations.Clear();
        }

        private void RestoreAllUnwrittenPoses()
        {
            foreach (Registration registration in _registrations.Values)
            {
                RestoreUnwrittenPose(registration);
            }
        }

        private static void RestoreUnwrittenPose(Registration registration)
        {
            if (!registration.HasPreviousOutput)
            {
                return;
            }

            RestoreBoneIfUnwritten(
                registration.Shoulder,
                registration.LastAppliedShoulderLocal,
                registration.LastSourceShoulderLocal);
            RestoreBoneIfUnwritten(
                registration.Arm,
                registration.LastAppliedArmLocal,
                registration.LastSourceArmLocal);
            RestoreBoneIfUnwritten(
                registration.LowerArm,
                registration.LastAppliedLowerArmLocal,
                registration.LastSourceLowerArmLocal);
            RestoreBoneIfUnwritten(
                registration.Hand,
                registration.LastAppliedHandLocal,
                registration.LastSourceHandLocal);
            registration.HasPreviousOutput = false;
        }

        private static void RestoreBoneIfUnwritten(
            Transform bone,
            Quaternion lastAppliedLocal,
            Quaternion lastSourceLocal)
        {
            if (bone != null &&
                Quaternion.Angle(
                    bone.localRotation,
                    lastAppliedLocal) <= 0.001f)
            {
                bone.localRotation = lastSourceLocal;
            }
        }

        private static void RecordOutput(
            Registration registration,
            Quaternion sourceShoulderLocal,
            Quaternion sourceArmLocal,
            Quaternion sourceLowerArmLocal,
            Quaternion sourceHandLocal)
        {
            registration.LastSourceShoulderLocal = sourceShoulderLocal;
            registration.LastSourceArmLocal = sourceArmLocal;
            registration.LastSourceLowerArmLocal = sourceLowerArmLocal;
            registration.LastSourceHandLocal = sourceHandLocal;
            registration.LastAppliedShoulderLocal =
                registration.Shoulder.localRotation;
            registration.LastAppliedArmLocal =
                registration.Arm.localRotation;
            registration.LastAppliedLowerArmLocal =
                registration.LowerArm.localRotation;
            registration.LastAppliedHandLocal =
                registration.Hand != null
                    ? registration.Hand.localRotation
                    : Quaternion.identity;
            registration.HasPreviousOutput = true;
        }

        private static float EvaluateCurve(
            AnimationCurve curve,
            float input)
        {
            return curve == null
                ? 0f
                : Mathf.Clamp01(curve.Evaluate(input));
        }

        private static Vector3 RotatePointAroundPivot(
            Vector3 point,
            Vector3 pivot,
            Quaternion rotation)
        {
            return pivot + rotation * (point - pivot);
        }

        private static void SetWorldRotation(
            Transform transformToRotate,
            Quaternion worldRotation)
        {
            if (transformToRotate == null)
            {
                return;
            }

            // Setting Transform.rotation lets Unity convert through the actual
            // parent chain, including Root/Global/Position or an external root.
            transformToRotate.rotation = worldRotation;
        }

        private static bool TryNormalize(ref Vector3 value)
        {
            float squaredMagnitude = value.sqrMagnitude;
            if (squaredMagnitude <= 0.0000000001f ||
                !IsFinite(value))
            {
                value = Vector3.zero;
                return false;
            }

            value /= Mathf.Sqrt(squaredMagnitude);
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return
                !float.IsNaN(value.x) &&
                !float.IsNaN(value.y) &&
                !float.IsNaN(value.z) &&
                !float.IsInfinity(value.x) &&
                !float.IsInfinity(value.y) &&
                !float.IsInfinity(value.z);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ShoulderControllerAnimator))]
    public sealed class ShoulderControllerAnimatorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            ShoulderControllerAnimator controller =
                (ShoulderControllerAnimator)target;
            if (changed)
            {
                controller.ValidateSettings();
                EditorUtility.SetDirty(controller);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The controller reads the final generated hierarchy and does " +
                "not reapply RaceData.FixupRotations. Root, Global, Position, " +
                "external skeleton roots, imported bone roll, and reflected " +
                "ancestry are handled through runtime transforms.",
                MessageType.Info);

            if (string.IsNullOrWhiteSpace(controller.ShoulderBoneName) ||
                string.IsNullOrWhiteSpace(controller.ArmBoneName))
            {
                EditorGUILayout.HelpBox(
                    "Shoulder Bone Name and Arm Bone Name are required.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Reset Response Curves"))
            {
                Undo.RecordObject(controller, "Reset Shoulder Response Curves");
                controller.ResetResponseCurves();
                EditorUtility.SetDirty(controller);
                serializedObject.Update();
            }
        }
    }
#endif
}
