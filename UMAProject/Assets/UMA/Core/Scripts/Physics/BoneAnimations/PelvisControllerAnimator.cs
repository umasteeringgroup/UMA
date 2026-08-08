using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public enum PelvisEndpointMode
    {
        FootWhenAvailable,
        UpperLegEndpoint
    }

    public enum PelvisFootIKMode
    {
        None,
        Automatic,
        UnityHumanoidPostSolve,
        GoalProvider,
        ExternalPostSolve
    }

    public enum PelvisLegSide
    {
        Left,
        Right
    }

    /// <summary>
    /// A world-space foot goal supplied by a grounding or Foot IK system.
    /// The provider produces goals; PelvisControllerRuntime remains the final
    /// pelvis and leg solver.
    /// </summary>
    public struct UMAFootIKState
    {
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;
        [Range(0f, 1f)]
        public float PositionWeight;
        [Range(0f, 1f)]
        public float RotationWeight;
        [Range(0f, 1f)]
        public float PlantedWeight;
        public bool HasKneeHint;
        public Vector3 KneeHintPosition;
        [Range(0f, 1f)]
        public float KneeHintWeight;
        public Vector3 GroundNormal;
    }

    /// <summary>
    /// Optional per-character source of Foot IK goals. Implement this on a
    /// MonoBehaviour on the UMA character or one of its children.
    /// </summary>
    public interface IUMAFootIKProvider
    {
        bool TryGetFootIKState(
            PelvisLegSide side,
            out UMAFootIKState state);
    }

    /// <summary>
    /// Forwards OnAnimatorIK when an UMAData and its Animator live on
    /// different GameObjects.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(8500)]
    public sealed class PelvisControllerIKBridge : MonoBehaviour
    {
        [NonSerialized]
        private PelvisControllerRuntime _runtime;

        internal void Bind(PelvisControllerRuntime runtime)
        {
            _runtime = runtime;
        }

        internal void Unbind(PelvisControllerRuntime runtime)
        {
            if (_runtime == runtime)
            {
                _runtime = null;
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_runtime != null)
            {
                _runtime.ProcessAnimatorIK(layerIndex);
            }
        }
    }

    /// <summary>
    /// Redistributes bilateral leg motion into the pelvis, stabilizes the
    /// configured spine chain, and solves both legs back toward their desired
    /// endpoints. Foot IK is optional and disabled by default.
    /// </summary>
    public class PelvisControllerAnimator : BaseUpdatedObject
    {
#if UNITY_EDITOR
        [MenuItem("Assets/Create/UMA/Physics/Pelvis Controller Animator")]
        public static void CreateAsset()
        {
            UMA.CustomAssetUtility.CreateAsset<PelvisControllerAnimator>();
        }
#endif

        [Header("Required Bones")]
        [Tooltip("Pelvis/Hips bone. Leave empty to use the Humanoid Hips mapping.")]
        public string HipsBoneName = string.Empty;

        [Tooltip("Left upper-leg bone. Leave empty to use the Humanoid mapping.")]
        public string LeftUpperLegBoneName = string.Empty;

        [Tooltip("Right upper-leg bone. Leave empty to use the Humanoid mapping.")]
        public string RightUpperLegBoneName = string.Empty;

        [Header("Optional Leg Overrides")]
        public string LeftLowerLegBoneName = string.Empty;
        public string RightLowerLegBoneName = string.Empty;
        public string LeftFootBoneName = string.Empty;
        public string RightFootBoneName = string.Empty;
        public string LeftToeBoneName = string.Empty;
        public string RightToeBoneName = string.Empty;

        [Header("Optional Torso Overrides")]
        [Tooltip("Spine bones ordered from pelvis toward the chest. Leave empty to use Humanoid Spine, Chest, and UpperChest mappings.")]
        public string[] SpineBoneNames = new string[0];

        [Tooltip("Head or upper-torso reference used to construct anatomical up. Leave empty for automatic resolution.")]
        public string UpperBodyReferenceBoneName = string.Empty;

        [Header("Endpoint")]
        public PelvisEndpointMode EndpointMode =
            PelvisEndpointMode.FootWhenAvailable;

        [Range(0f, 1f)]
        [Tooltip("Endpoint preservation when Foot IK is disabled. One keeps the animated feet fixed.")]
        public float AnimatedEndpointPreservation = 1f;

        [Range(0f, 1f)]
        [Tooltip("Endpoint preservation for a non-planted foot when Foot IK is enabled.")]
        public float SwingFootPreservation = 0.5f;

        [Tooltip("Restore the desired foot world rotation after solving each complete leg.")]
        public bool PreserveFootRotation = true;

        [Tooltip("Restore toe world rotations with the same weight as their feet.")]
        public bool PreserveToeRotation = true;

        [Header("Foot IK (Optional)")]
        [Tooltip("None is the default and requires no IK Pass, Humanoid avatar, or provider.")]
        public PelvisFootIKMode FootIKMode = PelvisFootIKMode.None;

        [Tooltip("In Goal Provider mode, submit provider goals to Unity Humanoid IK during OnAnimatorIK. Generic rigs are solved directly in LateUpdate.")]
        public bool ApplyProviderGoalsInAnimatorIK = true;

        [Min(-1)]
        [Tooltip("Animator Controller layer whose IK Pass receives provider goals. Minus one accepts every IK callback.")]
        public int AnimatorIKLayer = 0;

        [Range(0f, 1f)]
        [Tooltip("A foot at or above this planted weight begins contributing as the support foot.")]
        public float PlantThreshold = 0.75f;

        [Range(0f, 0.5f)]
        [Tooltip("A planted foot remains support-active until its weight falls this far below Plant Threshold.")]
        public float FootLockHysteresis = 0.1f;

        [Range(0f, 1f)]
        [Tooltip("Pelvis correction retained when Foot IK reports both feet airborne.")]
        public float AirborneEffect = 0f;

        [Tooltip("Use provider or Animator knee hints when available.")]
        public bool UseKneeHints = true;

        [Header("Pelvis Influence")]
        [Range(0f, 1f)]
        public float OverallEffect = 1f;

        [Range(0f, 1f)]
        public float StrideRotationEffect = 1f;

        [Range(0f, 1f)]
        public float ObliquityEffect = 0.25f;

        [Range(0f, 1f)]
        public float PelvicTiltEffect = 0f;

        [Header("Pelvis Limits")]
        [Min(0f)]
        public float MaximumStrideRotationDegrees = 6f;

        [Min(0f)]
        public float MaximumObliquityDegrees = 3f;

        [Min(0f)]
        public float MaximumPelvicTiltDegrees = 3f;

        [Min(0.001f)]
        [Tooltip("Normalized left/right stride difference that produces the full stride response.")]
        public float StrideForFullEffect = 0.65f;

        [Range(0f, 1f)]
        public float StrideDeadZone = 0.04f;

        public AnimationCurve StrideResponse = CreateDefaultUnitCurve();
        public AnimationCurve ObliquityResponse = CreateDefaultUnitCurve();
        public AnimationCurve PelvicTiltResponse =
            CreateDefaultSignedCurve();

        [Header("Torso Follow")]
        [Range(0f, 1f)]
        [Tooltip("Zero preserves the animated upper-torso yaw; one lets it inherit all added pelvis yaw.")]
        public float TorsoYawFollow = 0f;

        [Range(0f, 1f)]
        public float TorsoObliquityFollow = 0.25f;

        [Range(0f, 1f)]
        public float TorsoTiltFollow = 0.5f;

        [Header("Leg Solve")]
        [Min(0f)]
        [Tooltip("Minimum knee flexion retained by the two-bone solve.")]
        public float MinimumKneeFlexionDegrees = 2f;

        [Min(0.000001f)]
        public float EndpointTolerance = 0.0005f;

        [Range(8, 64)]
        [Tooltip("Samples used to find the largest contiguous pelvis influence reachable by both legs.")]
        public int ReachabilitySamples = 24;

        [Min(0f)]
        [Tooltip("Optional half-life for smoothing the three pelvis correction angles. Zero disables damping.")]
        public float DampingHalfLife = 0f;

        [Header("Activation")]
        [Tooltip("Optional Animator float parameter multiplied into Overall Effect.")]
        public string AnimatorWeightParameter = string.Empty;

        public bool DebugMode;

        public override void Initialize(UMAData umaData, SlotData slotData)
        {
            base.Initialize(umaData, slotData);
            ValidateSettings();

            if (umaData == null)
            {
                return;
            }

            PelvisControllerRuntime runtime =
                umaData.GetComponent<PelvisControllerRuntime>();
            if (runtime == null)
            {
                runtime =
                    umaData.gameObject.AddComponent<PelvisControllerRuntime>();
            }

            runtime.RegisterOrUpdate(umaData, this);
            initialized = true;
        }

        public override void DoUpdate(UMAData data, float step)
        {
            // PelvisControllerRuntime evaluates after animation in LateUpdate.
        }

        public void ValidateSettings()
        {
            AnimatedEndpointPreservation =
                Mathf.Clamp01(AnimatedEndpointPreservation);
            SwingFootPreservation = Mathf.Clamp01(SwingFootPreservation);
            PlantThreshold = Mathf.Clamp01(PlantThreshold);
            FootLockHysteresis =
                Mathf.Clamp(FootLockHysteresis, 0f, 0.5f);
            AirborneEffect = Mathf.Clamp01(AirborneEffect);
            OverallEffect = Mathf.Clamp01(OverallEffect);
            StrideRotationEffect = Mathf.Clamp01(StrideRotationEffect);
            ObliquityEffect = Mathf.Clamp01(ObliquityEffect);
            PelvicTiltEffect = Mathf.Clamp01(PelvicTiltEffect);
            MaximumStrideRotationDegrees =
                Mathf.Max(0f, MaximumStrideRotationDegrees);
            MaximumObliquityDegrees =
                Mathf.Max(0f, MaximumObliquityDegrees);
            MaximumPelvicTiltDegrees =
                Mathf.Max(0f, MaximumPelvicTiltDegrees);
            StrideForFullEffect = Mathf.Max(0.001f, StrideForFullEffect);
            StrideDeadZone = Mathf.Clamp01(StrideDeadZone);
            TorsoYawFollow = Mathf.Clamp01(TorsoYawFollow);
            TorsoObliquityFollow =
                Mathf.Clamp01(TorsoObliquityFollow);
            TorsoTiltFollow = Mathf.Clamp01(TorsoTiltFollow);
            MinimumKneeFlexionDegrees =
                Mathf.Clamp(MinimumKneeFlexionDegrees, 0f, 175f);
            EndpointTolerance = Mathf.Max(0.000001f, EndpointTolerance);
            ReachabilitySamples =
                Mathf.Clamp(ReachabilitySamples, 8, 64);
            DampingHalfLife = Mathf.Max(0f, DampingHalfLife);
            AnimatorIKLayer = Mathf.Max(-1, AnimatorIKLayer);

            if (SpineBoneNames == null)
            {
                SpineBoneNames = new string[0];
            }

            if (StrideResponse == null || StrideResponse.length == 0)
            {
                StrideResponse = CreateDefaultUnitCurve();
            }

            if (ObliquityResponse == null ||
                ObliquityResponse.length == 0)
            {
                ObliquityResponse = CreateDefaultUnitCurve();
            }

            if (PelvicTiltResponse == null ||
                PelvicTiltResponse.length == 0)
            {
                PelvicTiltResponse = CreateDefaultSignedCurve();
            }
        }

        public void ResetResponseCurves()
        {
            StrideResponse = CreateDefaultUnitCurve();
            ObliquityResponse = CreateDefaultUnitCurve();
            PelvicTiltResponse = CreateDefaultSignedCurve();
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        private static AnimationCurve CreateDefaultUnitCurve()
        {
            return AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        private static AnimationCurve CreateDefaultSignedCurve()
        {
            return new AnimationCurve(
                new Keyframe(-1f, -1f),
                new Keyframe(0f, 0f),
                new Keyframe(1f, 1f));
        }
    }

    /// <summary>
    /// Per-character pelvis, torso, and dual-leg solver. It runs before the
    /// Shoulder Controller and Twist Bone Manager.
    /// </summary>
    [DefaultExecutionOrder(8500)]
    [DisallowMultipleComponent]
    public sealed class PelvisControllerRuntime : MonoBehaviour
    {
        public struct AnatomicalFrame
        {
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Forward;
            public bool Reflected;
        }

        private sealed class BoneMemory
        {
            public Transform Bone;
            public Quaternion SourceLocal;
            public Quaternion AppliedLocal;
        }

        private sealed class LegChain
        {
            public PelvisLegSide Side;
            public Transform Upper;
            public Transform Lower;
            public Transform Foot;
            public Transform Toe;
            public Vector3 CachedBendDirection;
        }

        private sealed class Registration
        {
            public PelvisControllerAnimator Asset;
            public UMAData Data;
            public Animator Animator;
            public Transform CharacterRoot;
            public Transform Hips;
            public Transform UpperBodyReference;
            public Transform[] Spine = new Transform[0];
            public Quaternion[] SourceSpineWorld =
                new Quaternion[0];
            public LegChain Left;
            public LegChain Right;
            public BoneMemory[] Memories = new BoneMemory[0];

            public bool HasPreviousOutput;
            public float SmoothedYaw;
            public float SmoothedObliquity;
            public float SmoothedTilt;
            public bool WasReflected;
            public bool LeftPlantLocked;
            public bool RightPlantLocked;

            public IUMAFootIKProvider Provider;
            public UMAFootIKState CapturedLeftIK;
            public UMAFootIKState CapturedRightIK;
            public int CapturedIKFrame = -1;
            public int ProviderGoalsAppliedFrame = -1;
            public PelvisControllerIKBridge IKBridge;
            public int AnimatorWeightHash;
            public bool HasAnimatorWeight;
        }

        private struct LegSource
        {
            public LegChain Chain;
            public Vector3 UpperPosition;
            public Vector3 KneePosition;
            public Vector3 EndpointPosition;
            public Quaternion LowerWorldRotation;
            public Quaternion FootWorldRotation;
            public Quaternion ToeWorldRotation;
            public float UpperLength;
            public float LowerLength;
            public bool SolveFoot;
            public UMAFootIKState IK;
            public Vector3 FixedTarget;
            public Quaternion FixedRotation;
            public float Preservation;
            public float RotationPreservation;
            public Vector3 PolePosition;
        }

        private Registration _registration;
        private UMAData _umaData;
        private bool _subscribed;
        private IUMAFootIKProvider _providerOverride;

        [Tooltip("Disable when a custom animation pipeline calls EvaluateNow explicitly.")]
        public bool AutomaticUpdate = true;

        [Range(0f, 1f)]
        [Tooltip("Per-character runtime multiplier.")]
        public float RuntimeWeight = 1f;

        public int RegistrationCount
        {
            get { return _registration != null ? 1 : 0; }
        }

        public bool HasReflectedRegistration
        {
            get
            {
                return _registration != null &&
                       _registration.WasReflected;
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
            RestoreUnwrittenPose(_registration);
            Unsubscribe();
        }

        private void OnDestroy()
        {
            UnbindIKBridge(_registration);
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (AutomaticUpdate)
            {
                EvaluateNow();
            }
        }

        /// <summary>
        /// Captures Unity Humanoid IK goals and optionally submits goals from an
        /// IUMAFootIKProvider. None mode deliberately performs no IK work.
        /// </summary>
        private void OnAnimatorIK(int layerIndex)
        {
            ProcessAnimatorIK(layerIndex);
        }

        internal void ProcessAnimatorIK(int layerIndex)
        {
            Registration registration = _registration;
            if (!isActiveAndEnabled ||
                (_umaData != null && !_umaData.BoneAnimatorsEnabled) ||
                registration == null ||
                registration.Asset == null ||
                registration.Animator == null)
            {
                return;
            }

            PelvisControllerAnimator asset = registration.Asset;
            if (asset.FootIKMode == PelvisFootIKMode.None ||
                (asset.AnimatorIKLayer >= 0 &&
                 asset.AnimatorIKLayer != layerIndex) ||
                !IsHumanoid(registration.Animator))
            {
                return;
            }

            RefreshProvider(registration);
            bool providerMode =
                asset.FootIKMode == PelvisFootIKMode.GoalProvider ||
                (asset.FootIKMode == PelvisFootIKMode.Automatic &&
                 registration.Provider != null);

            UMAFootIKState left = default(UMAFootIKState);
            UMAFootIKState right = default(UMAFootIKState);
            bool hasLeftProvider =
                providerMode &&
                TryGetProviderState(
                    registration,
                    PelvisLegSide.Left,
                    out left);
            bool hasRightProvider =
                providerMode &&
                TryGetProviderState(
                    registration,
                    PelvisLegSide.Right,
                    out right);

            if (providerMode &&
                asset.ApplyProviderGoalsInAnimatorIK)
            {
                bool appliedProviderGoal = false;
                if (hasLeftProvider)
                {
                    ApplyAnimatorGoal(
                        registration.Animator,
                        PelvisLegSide.Left,
                        left,
                        asset.UseKneeHints);
                    appliedProviderGoal = true;
                }

                if (hasRightProvider)
                {
                    ApplyAnimatorGoal(
                        registration.Animator,
                        PelvisLegSide.Right,
                        right,
                        asset.UseKneeHints);
                    appliedProviderGoal = true;
                }

                if (appliedProviderGoal)
                {
                    registration.ProviderGoalsAppliedFrame =
                        Time.frameCount;
                }
            }

            if (!hasLeftProvider)
            {
                left = CaptureAnimatorGoal(
                    registration.Animator,
                    PelvisLegSide.Left);
            }

            if (!hasRightProvider)
            {
                right = CaptureAnimatorGoal(
                    registration.Animator,
                    PelvisLegSide.Right);
            }

            registration.CapturedLeftIK = SanitizeIKState(left);
            registration.CapturedRightIK = SanitizeIKState(right);
            registration.CapturedIKFrame = Time.frameCount;
        }

        public bool RegisterOrUpdate(
            UMAData data,
            PelvisControllerAnimator asset)
        {
            if (data == null || asset == null)
            {
                return false;
            }

            asset.ValidateSettings();
            _umaData = data;
            Subscribe();

            if (_registration != null &&
                _registration.Asset != null &&
                _registration.Asset != asset)
            {
                Debug.LogWarning(
                    "Only one Pelvis Controller can drive '" +
                    data.name + "'. Ignoring '" + asset.name +
                    "' because '" + _registration.Asset.name +
                    "' is already registered.",
                    data);
                return false;
            }

            if (_registration == null)
            {
                _registration = new Registration();
            }
            else
            {
                RestoreUnwrittenPose(_registration);
            }

            if (!TryResolveRegistration(data, asset, _registration))
            {
                UnbindIKBridge(_registration);
                _registration = null;
                return false;
            }

            _registration.HasPreviousOutput = false;
            _registration.SmoothedYaw = 0f;
            _registration.SmoothedObliquity = 0f;
            _registration.SmoothedTilt = 0f;
            _registration.Left.CachedBendDirection = Vector3.zero;
            _registration.Right.CachedBendDirection = Vector3.zero;
            _registration.LeftPlantLocked = false;
            _registration.RightPlantLocked = false;
            RefreshProvider(_registration);

            if (asset.DebugMode)
            {
                Debug.Log(
                    "Pelvis Controller registered '" + asset.name +
                    "' on '" + data.name + "'" +
                    (_registration.WasReflected
                        ? " with reflected ancestry detected."
                        : "."),
                    data);
            }

            return true;
        }

        public void SetFootIKProvider(MonoBehaviour provider)
        {
            _providerOverride = provider as IUMAFootIKProvider;
            if (_registration != null)
            {
                _registration.Provider = _providerOverride;
                RefreshProvider(_registration);
            }
        }

        public void EvaluateNow()
        {
            if (!isActiveAndEnabled || _registration == null)
            {
                return;
            }

            if (_umaData != null && !_umaData.BoneAnimatorsEnabled)
            {
                RestoreUnwrittenPose(_registration);
                return;
            }

            EvaluateRegistration(_registration);
        }

        private void EvaluateRegistration(Registration registration)
        {
            PelvisControllerAnimator asset = registration.Asset;
            if (asset == null ||
                registration.Hips == null ||
                registration.Left == null ||
                registration.Right == null)
            {
                return;
            }

            if (registration.Animator != null &&
                !registration.Animator.enabled)
            {
                RestoreUnwrittenPose(registration);
                return;
            }

            RestoreUnwrittenPose(registration);
            CaptureSourceLocals(registration);

            Quaternion sourceHipsWorld = registration.Hips.rotation;
            Vector3 sourceHipsPosition = registration.Hips.position;
            CaptureWorldRotations(
                registration.Spine,
                registration.SourceSpineWorld);

            AnatomicalFrame frame;
            if (!TryBuildAnatomicalFrame(registration, out frame))
            {
                RecordOutput(registration);
                return;
            }

            registration.WasReflected = frame.Reflected;

            LegSource left;
            LegSource right;
            if (!TryCaptureLegSource(
                    registration,
                    registration.Left,
                    frame,
                    out left) ||
                !TryCaptureLegSource(
                    registration,
                    registration.Right,
                    frame,
                    out right))
            {
                RecordOutput(registration);
                return;
            }

            float averageLegLength =
                (left.UpperLength +
                 left.LowerLength +
                 right.UpperLength +
                 right.LowerLength) * 0.25f;
            averageLegLength = Mathf.Max(0.000001f, averageLegLength);

            float leftForward = Vector3.Dot(
                left.EndpointPosition - sourceHipsPosition,
                frame.Forward) / averageLegLength;
            float rightForward = Vector3.Dot(
                right.EndpointPosition - sourceHipsPosition,
                frame.Forward) / averageLegLength;
            float strideSignal = Mathf.Clamp(
                (leftForward - rightForward) /
                asset.StrideForFullEffect,
                -1f,
                1f);
            if (Mathf.Abs(strideSignal) < asset.StrideDeadZone)
            {
                strideSignal = 0f;
            }
            else
            {
                strideSignal =
                    Mathf.Sign(strideSignal) *
                    Mathf.InverseLerp(
                        asset.StrideDeadZone,
                        1f,
                        Mathf.Abs(strideSignal));
            }

            float leftPlant = GetPlantWeight(
                left.IK,
                asset,
                ref registration.LeftPlantLocked);
            float rightPlant = GetPlantWeight(
                right.IK,
                asset,
                ref registration.RightPlantLocked);
            float supportDifference =
                Mathf.Clamp(rightPlant - leftPlant, -1f, 1f);
            float meanForward = Mathf.Clamp(
                (leftForward + rightForward) * 0.5f,
                -1f,
                1f);

            float activation =
                Mathf.Clamp01(asset.OverallEffect) *
                Mathf.Clamp01(RuntimeWeight) *
                GetAnimatorWeight(registration);
            bool hasActiveFootIK =
                asset.FootIKMode != PelvisFootIKMode.None &&
                (left.IK.Valid || right.IK.Valid);
            if (hasActiveFootIK)
            {
                activation *= Mathf.Lerp(
                    asset.AirborneEffect,
                    1f,
                    Mathf.Max(leftPlant, rightPlant));
            }

            float yaw = Mathf.Sign(strideSignal) *
                        asset.MaximumStrideRotationDegrees *
                        asset.StrideRotationEffect *
                        EvaluateUnitCurve(
                            asset.StrideResponse,
                            Mathf.Abs(strideSignal)) *
                        activation;
            float obliquity =
                Mathf.Sign(supportDifference) *
                asset.MaximumObliquityDegrees *
                asset.ObliquityEffect *
                EvaluateUnitCurve(
                    asset.ObliquityResponse,
                    Mathf.Abs(supportDifference)) *
                activation;
            float tilt =
                asset.MaximumPelvicTiltDegrees *
                asset.PelvicTiltEffect *
                EvaluateSignedCurve(
                    asset.PelvicTiltResponse,
                    meanForward) *
                activation;

            SmoothAngles(
                registration,
                asset,
                yaw,
                obliquity,
                tilt);

            Quaternion desiredDelta = BuildPelvisDelta(
                frame,
                registration.SmoothedYaw,
                registration.SmoothedObliquity,
                registration.SmoothedTilt);

            float influence = FindMaximumCoupledInfluence(
                sourceHipsPosition,
                desiredDelta,
                left,
                right,
                asset.MinimumKneeFlexionDegrees,
                asset.EndpointTolerance,
                asset.ReachabilitySamples);

            float appliedYaw = registration.SmoothedYaw * influence;
            float appliedObliquity =
                registration.SmoothedObliquity * influence;
            float appliedTilt = registration.SmoothedTilt * influence;
            Quaternion appliedDelta = BuildPelvisDelta(
                frame,
                appliedYaw,
                appliedObliquity,
                appliedTilt);

            SetWorldRotation(
                registration.Hips,
                appliedDelta * sourceHipsWorld);

            StabilizeSpine(
                registration,
                frame,
                registration.SourceSpineWorld,
                appliedYaw,
                appliedObliquity,
                appliedTilt);

            bool leftSolved = SolveLeg(
                registration,
                frame,
                left,
                sourceHipsPosition,
                appliedDelta,
                asset);
            bool rightSolved = SolveLeg(
                registration,
                frame,
                right,
                sourceHipsPosition,
                appliedDelta,
                asset);

            if (!leftSolved || !rightSolved)
            {
                RestoreSourceLocals(registration);
                registration.SmoothedYaw = 0f;
                registration.SmoothedObliquity = 0f;
                registration.SmoothedTilt = 0f;
            }

            if (asset.DebugMode)
            {
                LogEndpointError(registration, left, right, influence);
            }

            RecordOutput(registration);
        }

        private static void SmoothAngles(
            Registration registration,
            PelvisControllerAnimator asset,
            float yaw,
            float obliquity,
            float tilt)
        {
            if (asset.DampingHalfLife <= 0f ||
                !Application.isPlaying)
            {
                registration.SmoothedYaw = yaw;
                registration.SmoothedObliquity = obliquity;
                registration.SmoothedTilt = tilt;
                return;
            }

            float interpolation = 1f -
                Mathf.Pow(
                    0.5f,
                    Mathf.Max(0f, Time.deltaTime) /
                    Mathf.Max(
                        0.000001f,
                        asset.DampingHalfLife));
            registration.SmoothedYaw = Mathf.Lerp(
                registration.SmoothedYaw,
                yaw,
                interpolation);
            registration.SmoothedObliquity = Mathf.Lerp(
                registration.SmoothedObliquity,
                obliquity,
                interpolation);
            registration.SmoothedTilt = Mathf.Lerp(
                registration.SmoothedTilt,
                tilt,
                interpolation);
        }

        private static void StabilizeSpine(
            Registration registration,
            AnatomicalFrame frame,
            Quaternion[] sourceWorld,
            float yaw,
            float obliquity,
            float tilt)
        {
            Transform[] spine = registration.Spine;
            if (spine == null ||
                sourceWorld == null ||
                spine.Length == 0)
            {
                return;
            }

            PelvisControllerAnimator asset = registration.Asset;
            for (int index = 0; index < spine.Length; index++)
            {
                if (spine[index] == null)
                {
                    continue;
                }

                float progress = (index + 1f) / spine.Length;
                float yawFraction = Mathf.Lerp(
                    1f,
                    asset.TorsoYawFollow,
                    progress);
                float obliquityFraction = Mathf.Lerp(
                    1f,
                    asset.TorsoObliquityFollow,
                    progress);
                float tiltFraction = Mathf.Lerp(
                    1f,
                    asset.TorsoTiltFollow,
                    progress);
                Quaternion desiredDelta = BuildPelvisDelta(
                    frame,
                    yaw * yawFraction,
                    obliquity * obliquityFraction,
                    tilt * tiltFraction);
                SetWorldRotation(
                    spine[index],
                    desiredDelta * sourceWorld[index]);
            }
        }

        private static bool SolveLeg(
            Registration registration,
            AnatomicalFrame frame,
            LegSource source,
            Vector3 hipsPosition,
            Quaternion pelvisDelta,
            PelvisControllerAnimator asset)
        {
            Vector3 freeEndpoint = RotatePointAroundPivot(
                source.EndpointPosition,
                hipsPosition,
                pelvisDelta);
            Vector3 target = Vector3.Lerp(
                freeEndpoint,
                source.FixedTarget,
                source.Preservation);

            if (!source.SolveFoot)
            {
                Vector3 currentDirection =
                    source.Chain.Lower.position -
                    source.Chain.Upper.position;
                Vector3 desiredDirection =
                    target - source.Chain.Upper.position;
                if (!TryNormalize(ref currentDirection) ||
                    !TryNormalize(ref desiredDirection))
                {
                    return false;
                }

                Quaternion correction = Quaternion.FromToRotation(
                    currentDirection,
                    desiredDirection);
                SetWorldRotation(
                    source.Chain.Upper,
                    correction * source.Chain.Upper.rotation);
                SetWorldRotation(
                    source.Chain.Lower,
                    source.LowerWorldRotation);
                return true;
            }

            Vector3 rootPosition = source.Chain.Upper.position;
            Vector3 fallbackBend =
                source.Chain.CachedBendDirection;
            if (!TryNormalize(ref fallbackBend))
            {
                fallbackBend = frame.Forward;
            }

            Vector3 solvedKnee;
            Vector3 bendDirection;
            if (!TryCalculateTwoBoneJoint(
                    rootPosition,
                    target,
                    source.PolePosition,
                    fallbackBend,
                    source.UpperLength,
                    source.LowerLength,
                    asset.MinimumKneeFlexionDegrees,
                    out solvedKnee,
                    out bendDirection))
            {
                return false;
            }

            source.Chain.CachedBendDirection = bendDirection;

            Vector3 currentUpperDirection =
                source.Chain.Lower.position -
                source.Chain.Upper.position;
            Vector3 desiredUpperDirection =
                solvedKnee - source.Chain.Upper.position;
            if (!TryNormalize(ref currentUpperDirection) ||
                !TryNormalize(ref desiredUpperDirection))
            {
                return false;
            }

            Quaternion upperCorrection = Quaternion.FromToRotation(
                currentUpperDirection,
                desiredUpperDirection);
            SetWorldRotation(
                source.Chain.Upper,
                upperCorrection * source.Chain.Upper.rotation);

            Vector3 currentLowerDirection =
                source.Chain.Foot.position -
                source.Chain.Lower.position;
            Vector3 desiredLowerDirection =
                target - source.Chain.Lower.position;
            if (!TryNormalize(ref currentLowerDirection) ||
                !TryNormalize(ref desiredLowerDirection))
            {
                return false;
            }

            Quaternion lowerCorrection = Quaternion.FromToRotation(
                currentLowerDirection,
                desiredLowerDirection);
            SetWorldRotation(
                source.Chain.Lower,
                lowerCorrection * source.Chain.Lower.rotation);

            if (asset.PreserveFootRotation)
            {
                Quaternion footRotation = Quaternion.Slerp(
                    source.Chain.Foot.rotation,
                    source.FixedRotation,
                    source.RotationPreservation);
                SetWorldRotation(source.Chain.Foot, footRotation);
            }

            if (asset.PreserveToeRotation &&
                source.Chain.Toe != null)
            {
                Quaternion toeRotation = Quaternion.Slerp(
                    source.Chain.Toe.rotation,
                    source.ToeWorldRotation,
                    source.RotationPreservation);
                SetWorldRotation(source.Chain.Toe, toeRotation);
            }

            return true;
        }

        private static bool TryCaptureLegSource(
            Registration registration,
            LegChain chain,
            AnatomicalFrame frame,
            out LegSource source)
        {
            source = new LegSource
            {
                Chain = chain
            };

            if (chain == null ||
                chain.Upper == null ||
                chain.Lower == null)
            {
                return false;
            }

            PelvisControllerAnimator asset = registration.Asset;
            bool solveFoot =
                asset.EndpointMode ==
                    PelvisEndpointMode.FootWhenAvailable &&
                chain.Foot != null &&
                chain.Foot.IsChildOf(chain.Lower);

            source.UpperPosition = chain.Upper.position;
            source.KneePosition = chain.Lower.position;
            source.EndpointPosition = solveFoot
                ? chain.Foot.position
                : chain.Lower.position;
            source.LowerWorldRotation = chain.Lower.rotation;
            source.FootWorldRotation = chain.Foot != null
                ? chain.Foot.rotation
                : Quaternion.identity;
            source.ToeWorldRotation = chain.Toe != null
                ? chain.Toe.rotation
                : Quaternion.identity;
            source.UpperLength = Vector3.Distance(
                source.UpperPosition,
                source.KneePosition);
            source.LowerLength = solveFoot
                ? Vector3.Distance(
                    source.KneePosition,
                    source.EndpointPosition)
                : 0f;
            source.SolveFoot = solveFoot;

            if (source.UpperLength <= 0.000001f ||
                (solveFoot &&
                 source.LowerLength <= 0.000001f))
            {
                return false;
            }

            source.IK = ResolveFootIKState(
                registration,
                chain.Side,
                source);

            bool footIKEnabled =
                asset.FootIKMode != PelvisFootIKMode.None &&
                source.IK.Valid;
            if (!footIKEnabled)
            {
                source.FixedTarget = source.EndpointPosition;
                source.FixedRotation = source.FootWorldRotation;
                source.Preservation =
                    asset.AnimatedEndpointPreservation;
                source.RotationPreservation =
                    asset.AnimatedEndpointPreservation;
                source.PolePosition = source.KneePosition;
                return true;
            }

            float positionWeight = source.IK.Valid
                ? Mathf.Clamp01(source.IK.PositionWeight)
                : 0f;
            float rotationWeight = source.IK.Valid
                ? Mathf.Clamp01(source.IK.RotationWeight)
                : 0f;
            float plantWeight = source.IK.Valid
                ? Mathf.Clamp01(source.IK.PlantedWeight)
                : 0f;
            float constraintWeight =
                Mathf.Max(positionWeight, plantWeight);

            bool postSolved =
                asset.FootIKMode ==
                    PelvisFootIKMode.UnityHumanoidPostSolve ||
                asset.FootIKMode ==
                    PelvisFootIKMode.ExternalPostSolve ||
                registration.ProviderGoalsAppliedFrame ==
                    Time.frameCount ||
                (asset.FootIKMode ==
                     PelvisFootIKMode.Automatic &&
                 registration.Provider == null &&
                 registration.CapturedIKFrame ==
                     Time.frameCount);

            source.FixedTarget =
                source.IK.Valid && !postSolved
                    ? Vector3.Lerp(
                        source.EndpointPosition,
                        source.IK.Position,
                        positionWeight)
                    : source.EndpointPosition;
            source.FixedRotation =
                source.IK.Valid && !postSolved
                    ? Quaternion.Slerp(
                        source.FootWorldRotation,
                        source.IK.Rotation,
                        rotationWeight)
                    : source.FootWorldRotation;
            source.Preservation = Mathf.Lerp(
                asset.SwingFootPreservation,
                1f,
                constraintWeight);
            source.RotationPreservation = Mathf.Lerp(
                asset.SwingFootPreservation,
                1f,
                Mathf.Max(rotationWeight, plantWeight));
            source.PolePosition =
                source.IK.Valid &&
                asset.UseKneeHints &&
                source.IK.HasKneeHint
                    ? Vector3.Lerp(
                        source.KneePosition,
                        source.IK.KneeHintPosition,
                        Mathf.Clamp01(source.IK.KneeHintWeight))
                    : source.KneePosition;
            return true;
        }

        private static UMAFootIKState ResolveFootIKState(
            Registration registration,
            PelvisLegSide side,
            LegSource source)
        {
            PelvisControllerAnimator asset = registration.Asset;
            UMAFootIKState state;

            if ((asset.FootIKMode == PelvisFootIKMode.GoalProvider ||
                 asset.FootIKMode == PelvisFootIKMode.Automatic) &&
                TryGetProviderState(registration, side, out state))
            {
                return SanitizeIKState(state);
            }

            if ((asset.FootIKMode ==
                    PelvisFootIKMode.UnityHumanoidPostSolve ||
                 asset.FootIKMode == PelvisFootIKMode.Automatic) &&
                registration.CapturedIKFrame == Time.frameCount)
            {
                state = side == PelvisLegSide.Left
                    ? registration.CapturedLeftIK
                    : registration.CapturedRightIK;
                state.Position = source.EndpointPosition;
                state.Rotation = source.FootWorldRotation;
                state.Valid = true;
                state.PlantedWeight = Mathf.Max(
                    state.PlantedWeight,
                    state.PositionWeight);
                return SanitizeIKState(state);
            }

            if (asset.FootIKMode ==
                PelvisFootIKMode.ExternalPostSolve)
            {
                return new UMAFootIKState
                {
                    Valid = true,
                    Position = source.EndpointPosition,
                    Rotation = source.FootWorldRotation,
                    PositionWeight = 1f,
                    RotationWeight = 1f,
                    PlantedWeight = 1f,
                    GroundNormal = Vector3.up
                };
            }

            return new UMAFootIKState
            {
                Valid = false,
                Position = source.EndpointPosition,
                Rotation = source.FootWorldRotation,
                GroundNormal = Vector3.up
            };
        }

        private static float GetPlantWeight(
            UMAFootIKState state,
            PelvisControllerAnimator asset,
            ref bool locked)
        {
            if (asset.FootIKMode == PelvisFootIKMode.None ||
                !state.Valid)
            {
                locked = false;
                return 0f;
            }

            float weight = Mathf.Clamp01(
                state.PlantedWeight);
            float exitThreshold = Mathf.Max(
                0f,
                asset.PlantThreshold -
                asset.FootLockHysteresis);
            if (locked)
            {
                if (weight < exitThreshold)
                {
                    locked = false;
                }
            }
            else if (weight >= asset.PlantThreshold)
            {
                locked = true;
            }

            if (!locked)
            {
                return 0f;
            }

            return Mathf.InverseLerp(
                exitThreshold,
                1f,
                weight);
        }

        private static float FindMaximumCoupledInfluence(
            Vector3 hipsPosition,
            Quaternion desiredDelta,
            LegSource left,
            LegSource right,
            float minimumKneeFlexionDegrees,
            float tolerance,
            int samples)
        {
            if (!AreBothLegsReachable(
                    hipsPosition,
                    Quaternion.identity,
                    left,
                    right,
                    minimumKneeFlexionDegrees,
                    tolerance))
            {
                return 0f;
            }

            if (AreBothLegsReachable(
                    hipsPosition,
                    desiredDelta,
                    left,
                    right,
                    minimumKneeFlexionDegrees,
                    tolerance))
            {
                return 1f;
            }

            int sampleCount = Mathf.Clamp(samples, 8, 64);
            float low = 0f;
            float high = 1f;
            for (int sample = 1; sample <= sampleCount; sample++)
            {
                float candidate = sample / (float)sampleCount;
                Quaternion candidateDelta = Quaternion.Slerp(
                    Quaternion.identity,
                    desiredDelta,
                    candidate);
                if (AreBothLegsReachable(
                        hipsPosition,
                        candidateDelta,
                        left,
                        right,
                        minimumKneeFlexionDegrees,
                        tolerance))
                {
                    low = candidate;
                }
                else
                {
                    high = candidate;
                    break;
                }
            }

            for (int iteration = 0; iteration < 16; iteration++)
            {
                float midpoint = (low + high) * 0.5f;
                Quaternion midpointDelta = Quaternion.Slerp(
                    Quaternion.identity,
                    desiredDelta,
                    midpoint);
                if (AreBothLegsReachable(
                        hipsPosition,
                        midpointDelta,
                        left,
                        right,
                        minimumKneeFlexionDegrees,
                        tolerance))
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

        private static bool AreBothLegsReachable(
            Vector3 hipsPosition,
            Quaternion delta,
            LegSource left,
            LegSource right,
            float minimumKneeFlexionDegrees,
            float tolerance)
        {
            return IsLegReachable(
                       hipsPosition,
                       delta,
                       left,
                       minimumKneeFlexionDegrees,
                       tolerance) &&
                   IsLegReachable(
                       hipsPosition,
                       delta,
                       right,
                       minimumKneeFlexionDegrees,
                       tolerance);
        }

        private static bool IsLegReachable(
            Vector3 hipsPosition,
            Quaternion delta,
            LegSource source,
            float minimumKneeFlexionDegrees,
            float tolerance)
        {
            Vector3 candidateRoot = RotatePointAroundPivot(
                source.UpperPosition,
                hipsPosition,
                delta);
            Vector3 freeEndpoint = RotatePointAroundPivot(
                source.EndpointPosition,
                hipsPosition,
                delta);
            Vector3 target = Vector3.Lerp(
                freeEndpoint,
                source.FixedTarget,
                source.Preservation);

            if (!source.SolveFoot)
            {
                return Mathf.Abs(
                    Vector3.Distance(candidateRoot, target) -
                    source.UpperLength) <= tolerance;
            }

            float distance = Vector3.Distance(candidateRoot, target);
            float minimum = Mathf.Abs(
                source.UpperLength - source.LowerLength);
            float maximum = CalculateMaximumReach(
                source.UpperLength,
                source.LowerLength,
                minimumKneeFlexionDegrees);
            return distance >= minimum - tolerance &&
                   distance <= maximum + tolerance;
        }

        public static bool TryCalculateTwoBoneJoint(
            Vector3 rootPosition,
            Vector3 targetPosition,
            Vector3 polePosition,
            Vector3 fallbackBendDirection,
            float upperLength,
            float lowerLength,
            float minimumFlexionDegrees,
            out Vector3 jointPosition,
            out Vector3 bendDirection)
        {
            jointPosition = rootPosition;
            bendDirection = Vector3.zero;
            if (upperLength <= 0.000001f ||
                lowerLength <= 0.000001f)
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
            float minimumReach =
                Mathf.Abs(upperLength - lowerLength);
            float maximumReach = CalculateMaximumReach(
                upperLength,
                lowerLength,
                minimumFlexionDegrees);
            float clampedDistance = Mathf.Clamp(
                targetDistance,
                minimumReach + 0.000001f,
                maximumReach - 0.000001f);

            bendDirection = Vector3.ProjectOnPlane(
                polePosition - rootPosition,
                targetDirection);
            if (!TryNormalize(ref bendDirection))
            {
                bendDirection = Vector3.ProjectOnPlane(
                    fallbackBendDirection,
                    targetDirection);
                if (!TryNormalize(ref bendDirection))
                {
                    Vector3 fallbackAxis =
                        Mathf.Abs(
                            Vector3.Dot(
                                targetDirection,
                                Vector3.up)) < 0.95f
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
            float height = Mathf.Sqrt(
                Mathf.Max(
                    0f,
                    upperLength * upperLength - along * along));
            jointPosition =
                rootPosition +
                targetDirection * along +
                bendDirection * height;
            return IsFinite(jointPosition);
        }

        public static float CalculateMaximumReach(
            float upperLength,
            float lowerLength,
            float minimumFlexionDegrees)
        {
            float flexionRadians =
                Mathf.Clamp(
                    minimumFlexionDegrees,
                    0f,
                    175f) * Mathf.Deg2Rad;
            float squared =
                upperLength * upperLength +
                lowerLength * lowerLength +
                2f * upperLength * lowerLength *
                Mathf.Cos(flexionRadians);
            return Mathf.Sqrt(Mathf.Max(0f, squared));
        }

        private bool TryResolveRegistration(
            UMAData data,
            PelvisControllerAnimator asset,
            Registration registration)
        {
            if (data.skeleton == null)
            {
                Debug.LogWarning(
                    "Pelvis Controller cannot initialize because '" +
                    data.name + "' has no UMA skeleton.",
                    data);
                return false;
            }

            Animator animator = data.animator != null
                ? data.animator
                : data.GetComponent<Animator>();

            Transform hips =
                ResolveNamedBone(data, asset.HipsBoneName) ??
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.Hips) ??
                FindBoneByTokens(
                    data.skeleton.GetRootTransform(),
                    "hips",
                    "pelvis");
            Transform leftUpper =
                ResolveNamedBone(
                    data,
                    asset.LeftUpperLegBoneName) ??
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.LeftUpperLeg);
            Transform rightUpper =
                ResolveNamedBone(
                    data,
                    asset.RightUpperLegBoneName) ??
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.RightUpperLeg);

            if (hips == null || leftUpper == null || rightUpper == null)
            {
                Debug.LogWarning(
                    "Pelvis Controller '" + asset.name +
                    "' could not resolve Hips and both upper legs on '" +
                    data.name + "'.",
                    data);
                return false;
            }

            if (!leftUpper.IsChildOf(hips) ||
                !rightUpper.IsChildOf(hips))
            {
                Debug.LogWarning(
                    "Pelvis Controller upper legs must be descendants of '" +
                    hips.name + "'.",
                    data);
                return false;
            }

            LegChain left = ResolveLeg(
                data,
                animator,
                PelvisLegSide.Left,
                leftUpper,
                asset.LeftLowerLegBoneName,
                asset.LeftFootBoneName,
                asset.LeftToeBoneName);
            LegChain right = ResolveLeg(
                data,
                animator,
                PelvisLegSide.Right,
                rightUpper,
                asset.RightLowerLegBoneName,
                asset.RightFootBoneName,
                asset.RightToeBoneName);
            if (left == null || right == null)
            {
                Debug.LogWarning(
                    "Pelvis Controller could not resolve both lower-leg descendants on '" +
                    data.name + "'.",
                    data);
                return false;
            }

            Transform upperReference =
                ResolveNamedBone(
                    data,
                    asset.UpperBodyReferenceBoneName) ??
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.Head) ??
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.UpperChest) ??
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.Chest) ??
                ResolveHumanoidBone(
                    animator,
                    HumanBodyBones.Spine);

            Transform[] spine = ResolveSpineChain(
                data,
                animator,
                hips,
                asset.SpineBoneNames);

            registration.Asset = asset;
            registration.Data = data;
            registration.Animator = animator;
            registration.CharacterRoot = data.transform;
            registration.Hips = hips;
            registration.UpperBodyReference =
                upperReference != null
                    ? upperReference
                    : spine.Length > 0
                        ? spine[spine.Length - 1]
                        : hips;
            registration.Spine = spine;
            registration.SourceSpineWorld =
                new Quaternion[spine.Length];
            registration.Left = left;
            registration.Right = right;
            registration.Memories = BuildBoneMemories(
                hips,
                spine,
                left,
                right);
            registration.WasReflected =
                IsReflected(hips.localToWorldMatrix);
            EnsureIKBridge(registration);

            registration.AnimatorWeightHash = 0;
            registration.HasAnimatorWeight = false;
            if (animator != null &&
                !string.IsNullOrWhiteSpace(
                    asset.AnimatorWeightParameter))
            {
                string parameterName =
                    asset.AnimatorWeightParameter.Trim();
                int parameterHash =
                    Animator.StringToHash(parameterName);
                AnimatorControllerParameter[] parameters =
                    animator.parameters;
                for (int index = 0;
                     index < parameters.Length;
                     index++)
                {
                    if (parameters[index].nameHash ==
                            parameterHash &&
                        parameters[index].type ==
                            AnimatorControllerParameterType.Float)
                    {
                        registration.AnimatorWeightHash =
                            parameterHash;
                        registration.HasAnimatorWeight = true;
                        break;
                    }
                }
            }

            return true;
        }

        private void EnsureIKBridge(Registration registration)
        {
            Animator animator = registration.Animator;
            if (registration.IKBridge != null &&
                (animator == null ||
                 registration.IKBridge.gameObject !=
                    animator.gameObject))
            {
                registration.IKBridge.Unbind(this);
                registration.IKBridge = null;
            }

            if (animator == null ||
                animator.gameObject == gameObject)
            {
                return;
            }

            PelvisControllerIKBridge bridge =
                animator.GetComponent<PelvisControllerIKBridge>();
            if (bridge == null)
            {
                bridge =
                    animator.gameObject.AddComponent<
                        PelvisControllerIKBridge>();
            }

            bridge.Bind(this);
            registration.IKBridge = bridge;
        }

        private void UnbindIKBridge(Registration registration)
        {
            if (registration != null &&
                registration.IKBridge != null)
            {
                registration.IKBridge.Unbind(this);
                registration.IKBridge = null;
            }
        }

        private static LegChain ResolveLeg(
            UMAData data,
            Animator animator,
            PelvisLegSide side,
            Transform upper,
            string lowerName,
            string footName,
            string toeName)
        {
            bool left = side == PelvisLegSide.Left;
            Transform lower =
                ResolveNamedBone(data, lowerName) ??
                ResolveHumanoidBone(
                    animator,
                    left
                        ? HumanBodyBones.LeftLowerLeg
                        : HumanBodyBones.RightLowerLeg);
            if (lower == null || !lower.IsChildOf(upper))
            {
                lower = FindBoneByTokens(
                    upper,
                    "lowerleg",
                    "leg",
                    "shin",
                    "calf",
                    "knee");
            }

            if (lower == null || !lower.IsChildOf(upper))
            {
                return null;
            }

            Transform foot =
                ResolveNamedBone(data, footName) ??
                ResolveHumanoidBone(
                    animator,
                    left
                        ? HumanBodyBones.LeftFoot
                        : HumanBodyBones.RightFoot);
            if (foot == null || !foot.IsChildOf(lower))
            {
                foot = FindBoneByTokens(
                    lower,
                    "foot",
                    "ankle");
            }

            if (foot != null && !foot.IsChildOf(lower))
            {
                foot = null;
            }

            Transform toe =
                ResolveNamedBone(data, toeName) ??
                ResolveHumanoidBone(
                    animator,
                    left
                        ? HumanBodyBones.LeftToes
                        : HumanBodyBones.RightToes);
            if (toe == null && foot != null)
            {
                toe = FindBoneByTokens(
                    foot,
                    "toe",
                    "ball");
            }

            if (toe != null &&
                (foot == null || !toe.IsChildOf(foot)))
            {
                toe = null;
            }

            return new LegChain
            {
                Side = side,
                Upper = upper,
                Lower = lower,
                Foot = foot,
                Toe = toe
            };
        }

        private static Transform[] ResolveSpineChain(
            UMAData data,
            Animator animator,
            Transform hips,
            string[] configuredNames)
        {
            List<Transform> spine = new List<Transform>();
            if (configuredNames != null &&
                configuredNames.Length > 0)
            {
                for (int index = 0;
                     index < configuredNames.Length;
                     index++)
                {
                    AddUniqueDescendant(
                        spine,
                        ResolveNamedBone(
                            data,
                            configuredNames[index]),
                        hips);
                }
            }
            else
            {
                AddUniqueDescendant(
                    spine,
                    ResolveHumanoidBone(
                        animator,
                        HumanBodyBones.Spine),
                    hips);
                AddUniqueDescendant(
                    spine,
                    ResolveHumanoidBone(
                        animator,
                        HumanBodyBones.Chest),
                    hips);
                AddUniqueDescendant(
                    spine,
                    ResolveHumanoidBone(
                        animator,
                        HumanBodyBones.UpperChest),
                    hips);
            }

            spine.Sort(
                delegate(Transform first, Transform second)
                {
                    return GetDepth(first).CompareTo(
                        GetDepth(second));
                });
            return spine.ToArray();
        }

        private static void AddUniqueDescendant(
            List<Transform> list,
            Transform candidate,
            Transform ancestor)
        {
            if (candidate != null &&
                candidate.IsChildOf(ancestor) &&
                !list.Contains(candidate))
            {
                list.Add(candidate);
            }
        }

        private static int GetDepth(Transform transform)
        {
            int depth = 0;
            while (transform != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        private static BoneMemory[] BuildBoneMemories(
            Transform hips,
            Transform[] spine,
            LegChain left,
            LegChain right)
        {
            List<BoneMemory> memories =
                new List<BoneMemory>();
            HashSet<Transform> seen = new HashSet<Transform>();
            AddMemory(memories, seen, hips);
            if (spine != null)
            {
                for (int index = 0; index < spine.Length; index++)
                {
                    AddMemory(memories, seen, spine[index]);
                }
            }

            AddLegMemories(memories, seen, left);
            AddLegMemories(memories, seen, right);
            return memories.ToArray();
        }

        private static void AddLegMemories(
            List<BoneMemory> memories,
            HashSet<Transform> seen,
            LegChain chain)
        {
            if (chain == null)
            {
                return;
            }

            AddMemory(memories, seen, chain.Upper);
            AddMemory(memories, seen, chain.Lower);
            AddMemory(memories, seen, chain.Foot);
            AddMemory(memories, seen, chain.Toe);
        }

        private static void AddMemory(
            List<BoneMemory> memories,
            HashSet<Transform> seen,
            Transform bone)
        {
            if (bone != null && seen.Add(bone))
            {
                memories.Add(new BoneMemory { Bone = bone });
            }
        }

        private static bool TryBuildAnatomicalFrame(
            Registration registration,
            out AnatomicalFrame frame)
        {
            Vector3 up =
                registration.UpperBodyReference.position -
                registration.Hips.position;
            if (!TryNormalize(ref up))
            {
                up = registration.CharacterRoot != null
                    ? registration.CharacterRoot.up
                    : Vector3.up;
            }

            Vector3 right =
                registration.Right.Upper.position -
                registration.Left.Upper.position;
            return TryBuildAnatomicalFrame(
                up,
                right,
                IsReflected(
                    registration.Hips.localToWorldMatrix),
                out frame);
        }

        public static bool TryBuildAnatomicalFrame(
            Vector3 upDirection,
            Vector3 rightDirection,
            bool reflected,
            out AnatomicalFrame frame)
        {
            frame = new AnatomicalFrame
            {
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

            rightDirection = Vector3.Cross(
                upDirection,
                forward);
            if (!TryNormalize(ref rightDirection))
            {
                return false;
            }

            frame.Right = rightDirection;
            frame.Up = upDirection;
            frame.Forward = forward;
            return true;
        }

        public static bool IsReflected(Matrix4x4 matrix)
        {
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);
            return Vector3.Dot(Vector3.Cross(x, y), z) < 0f;
        }

        private static Quaternion BuildPelvisDelta(
            AnatomicalFrame frame,
            float yaw,
            float obliquity,
            float tilt)
        {
            Quaternion yawDelta =
                Quaternion.AngleAxis(yaw, frame.Up);
            Quaternion obliquityDelta =
                Quaternion.AngleAxis(
                    obliquity,
                    frame.Forward);
            Quaternion tiltDelta =
                Quaternion.AngleAxis(tilt, frame.Right);
            return tiltDelta * obliquityDelta * yawDelta;
        }

        private static void ApplyAnimatorGoal(
            Animator animator,
            PelvisLegSide side,
            UMAFootIKState state,
            bool useKneeHint)
        {
            if (!state.Valid)
            {
                return;
            }

            AvatarIKGoal goal = side == PelvisLegSide.Left
                ? AvatarIKGoal.LeftFoot
                : AvatarIKGoal.RightFoot;
            animator.SetIKPositionWeight(
                goal,
                Mathf.Clamp01(state.PositionWeight));
            animator.SetIKRotationWeight(
                goal,
                Mathf.Clamp01(state.RotationWeight));
            animator.SetIKPosition(goal, state.Position);
            animator.SetIKRotation(goal, state.Rotation);

            if (useKneeHint && state.HasKneeHint)
            {
                AvatarIKHint hint =
                    side == PelvisLegSide.Left
                        ? AvatarIKHint.LeftKnee
                        : AvatarIKHint.RightKnee;
                animator.SetIKHintPositionWeight(
                    hint,
                    Mathf.Clamp01(state.KneeHintWeight));
                animator.SetIKHintPosition(
                    hint,
                    state.KneeHintPosition);
            }
        }

        private static UMAFootIKState CaptureAnimatorGoal(
            Animator animator,
            PelvisLegSide side)
        {
            AvatarIKGoal goal = side == PelvisLegSide.Left
                ? AvatarIKGoal.LeftFoot
                : AvatarIKGoal.RightFoot;
            AvatarIKHint hint = side == PelvisLegSide.Left
                ? AvatarIKHint.LeftKnee
                : AvatarIKHint.RightKnee;
            float positionWeight =
                animator.GetIKPositionWeight(goal);
            float rotationWeight =
                animator.GetIKRotationWeight(goal);
            float hintWeight =
                animator.GetIKHintPositionWeight(hint);
            return new UMAFootIKState
            {
                Valid =
                    positionWeight > 0f ||
                    rotationWeight > 0f ||
                    hintWeight > 0f,
                Position = animator.GetIKPosition(goal),
                Rotation = animator.GetIKRotation(goal),
                PositionWeight = positionWeight,
                RotationWeight = rotationWeight,
                PlantedWeight = positionWeight,
                HasKneeHint = hintWeight > 0f,
                KneeHintPosition =
                    animator.GetIKHintPosition(hint),
                KneeHintWeight = hintWeight,
                GroundNormal = Vector3.up
            };
        }

        private static UMAFootIKState SanitizeIKState(
            UMAFootIKState state)
        {
            state.PositionWeight =
                Mathf.Clamp01(state.PositionWeight);
            state.RotationWeight =
                Mathf.Clamp01(state.RotationWeight);
            state.PlantedWeight =
                Mathf.Clamp01(state.PlantedWeight);
            state.KneeHintWeight =
                Mathf.Clamp01(state.KneeHintWeight);
            if (!IsFinite(state.Position) ||
                !IsFinite(state.KneeHintPosition) ||
                !IsFinite(state.GroundNormal))
            {
                state.Valid = false;
            }

            if (state.Rotation == default(Quaternion) ||
                !IsFinite(state.Rotation))
            {
                state.Rotation = Quaternion.identity;
            }

            if (!TryNormalize(ref state.GroundNormal))
            {
                state.GroundNormal = Vector3.up;
            }

            return state;
        }

        private static bool TryGetProviderState(
            Registration registration,
            PelvisLegSide side,
            out UMAFootIKState state)
        {
            state = default(UMAFootIKState);
            return registration.Provider != null &&
                   registration.Provider.TryGetFootIKState(
                       side,
                       out state) &&
                   state.Valid;
        }

        private void RefreshProvider(Registration registration)
        {
            if (registration == null)
            {
                return;
            }

            if (_providerOverride != null)
            {
                registration.Provider = _providerOverride;
                return;
            }

            if (registration.Provider is UnityEngine.Object existing &&
                existing != null)
            {
                return;
            }

            registration.Provider = null;
            MonoBehaviour[] behaviours =
                registration.Data.GetComponentsInChildren<MonoBehaviour>(
                    true);
            for (int index = 0;
                 index < behaviours.Length;
                 index++)
            {
                IUMAFootIKProvider provider =
                    behaviours[index] as IUMAFootIKProvider;
                if (provider != null)
                {
                    registration.Provider = provider;
                    break;
                }
            }
        }

        private static float GetAnimatorWeight(
            Registration registration)
        {
            if (!registration.HasAnimatorWeight ||
                registration.Animator == null)
            {
                return 1f;
            }

            return Mathf.Clamp01(
                registration.Animator.GetFloat(
                    registration.AnimatorWeightHash));
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
            if (!IsHumanoid(animator))
            {
                return null;
            }

            return animator.GetBoneTransform(bone);
        }

        private static bool IsHumanoid(Animator animator)
        {
            return animator != null &&
                   animator.avatar != null &&
                   animator.avatar.isHuman;
        }

        private static Transform FindBoneByTokens(
            Transform root,
            params string[] tokens)
        {
            if (root == null)
            {
                return null;
            }

            Transform[] descendants =
                root.GetComponentsInChildren<Transform>(true);
            for (int tokenIndex = 0;
                 tokenIndex < tokens.Length;
                 tokenIndex++)
            {
                for (int index = 0;
                     index < descendants.Length;
                     index++)
                {
                    Transform candidate = descendants[index];
                    if (candidate == root)
                    {
                        continue;
                    }

                    if (candidate.name.IndexOf(
                            tokens[tokenIndex],
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate;
                    }
                }
            }

            return root.childCount > 0
                ? root.GetChild(0)
                : null;
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
            RestoreUnwrittenPose(_registration);
            UnbindIKBridge(_registration);
            _registration = null;
        }

        private static void CaptureSourceLocals(
            Registration registration)
        {
            BoneMemory[] memories = registration.Memories;
            for (int index = 0; index < memories.Length; index++)
            {
                if (memories[index].Bone != null)
                {
                    memories[index].SourceLocal =
                        memories[index].Bone.localRotation;
                }
            }
        }

        private static void RestoreSourceLocals(
            Registration registration)
        {
            BoneMemory[] memories = registration.Memories;
            for (int index = 0; index < memories.Length; index++)
            {
                if (memories[index].Bone != null)
                {
                    memories[index].Bone.localRotation =
                        memories[index].SourceLocal;
                }
            }
        }

        private static void RestoreUnwrittenPose(
            Registration registration)
        {
            if (registration == null ||
                !registration.HasPreviousOutput)
            {
                return;
            }

            BoneMemory[] memories = registration.Memories;
            for (int index = 0; index < memories.Length; index++)
            {
                BoneMemory memory = memories[index];
                if (memory.Bone != null &&
                    Quaternion.Angle(
                        memory.Bone.localRotation,
                        memory.AppliedLocal) <= 0.001f)
                {
                    memory.Bone.localRotation =
                        memory.SourceLocal;
                }
            }

            registration.HasPreviousOutput = false;
        }

        private static void RecordOutput(
            Registration registration)
        {
            BoneMemory[] memories = registration.Memories;
            for (int index = 0; index < memories.Length; index++)
            {
                if (memories[index].Bone != null)
                {
                    memories[index].AppliedLocal =
                        memories[index].Bone.localRotation;
                }
            }

            registration.HasPreviousOutput = true;
        }

        private static void CaptureWorldRotations(
            Transform[] transforms,
            Quaternion[] rotations)
        {
            if (transforms == null ||
                rotations == null ||
                transforms.Length != rotations.Length)
            {
                return;
            }

            for (int index = 0;
                 index < transforms.Length;
                 index++)
            {
                rotations[index] = transforms[index] != null
                    ? transforms[index].rotation
                    : Quaternion.identity;
            }
        }

        private static void LogEndpointError(
            Registration registration,
            LegSource left,
            LegSource right,
            float influence)
        {
            PelvisControllerAnimator asset = registration.Asset;
            float leftError = left.Preservation >= 0.999f
                ? Vector3.Distance(
                    left.Chain.Foot != null
                        ? left.Chain.Foot.position
                        : left.Chain.Lower.position,
                    left.FixedTarget)
                : 0f;
            float rightError = right.Preservation >= 0.999f
                ? Vector3.Distance(
                    right.Chain.Foot != null
                        ? right.Chain.Foot.position
                        : right.Chain.Lower.position,
                    right.FixedTarget)
                : 0f;
            if (leftError > asset.EndpointTolerance * 2f ||
                rightError > asset.EndpointTolerance * 2f)
            {
                Debug.LogWarning(
                    "Pelvis Controller endpoint error on '" +
                    registration.Data.name + "': left " +
                    leftError.ToString("G6") + ", right " +
                    rightError.ToString("G6") +
                    " (influence " +
                    influence.ToString("F3") + ").",
                    registration.Data);
            }
        }

        private static float EvaluateUnitCurve(
            AnimationCurve curve,
            float input)
        {
            return curve == null
                ? 0f
                : Mathf.Clamp01(
                    curve.Evaluate(Mathf.Clamp01(input)));
        }

        private static float EvaluateSignedCurve(
            AnimationCurve curve,
            float input)
        {
            return curve == null
                ? 0f
                : Mathf.Clamp(
                    curve.Evaluate(
                        Mathf.Clamp(input, -1f, 1f)),
                    -1f,
                    1f);
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
            if (transformToRotate != null)
            {
                transformToRotate.rotation = worldRotation;
            }
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

        private static bool IsFinite(Quaternion value)
        {
            return
                !float.IsNaN(value.x) &&
                !float.IsNaN(value.y) &&
                !float.IsNaN(value.z) &&
                !float.IsNaN(value.w) &&
                !float.IsInfinity(value.x) &&
                !float.IsInfinity(value.y) &&
                !float.IsInfinity(value.z) &&
                !float.IsInfinity(value.w);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(PelvisControllerAnimator))]
    public sealed class PelvisControllerAnimatorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            PelvisControllerAnimator controller =
                (PelvisControllerAnimator)target;
            if (changed)
            {
                controller.ValidateSettings();
                EditorUtility.SetDirty(controller);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Foot IK is optional and defaults to None. None mode " +
                "requires no IK Pass, Humanoid avatar, provider, or " +
                "raycasts. Unity Humanoid modes require IK Pass on the " +
                "selected Animator Controller layer.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "The controller reads final generated world transforms. " +
                "Root, Global, Position, external roots, imported bone " +
                "roll, and reflected ancestry are already included; " +
                "RaceData.FixupRotations is not applied again.",
                MessageType.Info);

            if (controller.FootIKMode ==
                    PelvisFootIKMode.GoalProvider ||
                controller.FootIKMode ==
                    PelvisFootIKMode.Automatic)
            {
                EditorGUILayout.HelpBox(
                    "Goal Provider mode discovers an " +
                    "IUMAFootIKProvider on the character or its children. " +
                    "On a Humanoid rig it can submit those goals during " +
                    "OnAnimatorIK; Generic rigs are solved directly.",
                    MessageType.None);
            }

            if (GUILayout.Button("Reset Response Curves"))
            {
                Undo.RecordObject(
                    controller,
                    "Reset Pelvis Response Curves");
                controller.ResetResponseCurves();
                EditorUtility.SetDirty(controller);
                serializedObject.Update();
            }
        }
    }
#endif
}
