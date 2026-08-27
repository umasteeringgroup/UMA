using System;
using UnityEngine;

namespace UMA.Dismemberment
{
    public enum RuntimeDecalState
    {
        Emitting,
        Flowing,
        Settling,
        Holding,
        Fading,
        Complete
    }

    public enum SurfaceFluidDetachedRoute
    {
        SourceBody,
        SharedAtlas,
        IndependentDetachedPiece
    }

    public enum RuntimeSurfaceDebugTexture
    {
        CompositedOutput,
        SurfaceWorldPosition,
        SurfaceFlow,
        InjectionMask,
        SeamLinks,
        MobileFluidState
    }

    [Flags]
    public enum SurfaceFluidChannels
    {
        Albedo = 1,
        Normal = 2,
        Wetness = 4
    }

    [Serializable]
    public struct RuntimeDecalHandle : IEquatable<RuntimeDecalHandle>
    {
        [SerializeField] private long controllerSession;
        [SerializeField] private long sequence;

        public bool IsValid => controllerSession > 0 && sequence > 0;
        public long ControllerSession => controllerSession;
        public long Sequence => sequence;

        internal RuntimeDecalHandle(long controllerSession, long sequence)
        {
            this.controllerSession = controllerSession;
            this.sequence = sequence;
        }

        public bool Equals(RuntimeDecalHandle other) =>
            controllerSession == other.controllerSession && sequence == other.sequence;
        public override bool Equals(object obj) => obj is RuntimeDecalHandle other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (controllerSession.GetHashCode() * 397) ^ sequence.GetHashCode(); }
        }
        public static bool operator ==(RuntimeDecalHandle left, RuntimeDecalHandle right) =>
            left.Equals(right);
        public static bool operator !=(RuntimeDecalHandle left, RuntimeDecalHandle right) =>
            !left.Equals(right);
    }

    [Serializable]
    public struct RuntimeDecalFadeSettings
    {
        [Min(0f)] public float holdSeconds;
        [Min(0.01f)] public float fadeSeconds;
        public AnimationCurve opacity;

        public static RuntimeDecalFadeSettings Default => new RuntimeDecalFadeSettings
        {
            holdSeconds = 5f,
            fadeSeconds = 3f,
            opacity = AnimationCurve.Linear(0f, 1f, 1f, 0f)
        };

        internal float Evaluate(float normalizedTime)
        {
            float value = opacity != null && opacity.length > 0
                ? opacity.Evaluate(Mathf.Clamp01(normalizedTime))
                : 1f - Mathf.Clamp01(normalizedTime);
            return Mathf.Clamp01(value);
        }
    }

    [CreateAssetMenu(menuName = "UMA/Dismemberment/Surface Fluid Profile",
        fileName = "Surface Fluid Profile")]
    public sealed class UMASurfaceFluidProfile : ScriptableObject
    {
        [Header("Appearance")]
        public Color color = new Color(0.32f, 0.005f, 0.003f, 0.92f);
        [Min(1f), Tooltip("Converts simulated film thickness to visible opacity. Higher values " +
            "make thin trails darker without increasing the amount or width of fluid.")]
        public float appearanceThicknessScale = 12000f;
        [Min(0f), Tooltip("Film thinner than this is not rendered. This suppresses faint " +
            "bilinear haze around a trail without deleting fluid from the simulation.")]
        public float appearanceThicknessThreshold = 0.000002f;
        [Min(1f), Tooltip("Optical-density multiplier applied only to deposited trail fluid. " +
            "This makes a thin streak approach the opacity of the source droplet without " +
            "changing its simulated width, speed, or volume.")]
        public float depositedTrailOpacityBoost = 8f;
        [Range(0f, 1f), Tooltip("Final opacity multiplier for deposited trail fluid. A value " +
            "of 1 lets a sufficiently dense trail reach the same alpha as the moving droplet.")]
        public float depositedTrailAlpha = 0.95f;
        [Tooltip("Optional multi-channel UMA overlay. The first compatible albedo texture is " +
            "used as breakup/appearance modulation; its alpha mask is shared.")]
        public OverlayDataAsset sourceOverlay;
        public SurfaceFluidChannels channels = SurfaceFluidChannels.Albedo;
        [Tooltip("Required when Wetness is enabled. It must name a dedicated scalar wetness or " +
            "smoothness texture property; packed mask maps are not modified implicitly.")]
        public string wetnessMaterialPropertyName;
        public string[] targetSlotGroups = Array.Empty<string>();
        public string[] targetOverlayGroups = Array.Empty<string>();

        [Header("Emission and Lifetime")]
        [Min(0f)] public float emissionDuration = 1.25f;
        [Min(0f)] public float emissionRate = 0.0015f;
        [Min(0.0001f)] public float emissionRadiusMeters = 0.003f;
        [Min(0.05f)] public float mobileLifetime = 8f;
        [Min(0f)] public float holdingDuration = 20f;
        [Min(0.01f)] public float fadeDuration = 8f;

        [Header("Flow (1 Unity Unit = 1 Meter)")]
        [Min(0f)] public float fallSpeedMetersPerSecond = 0.08f;
        [Min(0.001f)] public float maximumTravelMeters = 1.5f;
        [Range(0f, 1f)] public float viscosity = 0.45f;
        [Range(0f, 1f)] public float adhesion = 0.35f;
        [Range(0f, 1f)] public float lateralSpread = 0.035f;
        [Range(0f, 1f)] public float pooling = 0.65f;
        [Min(0f), Tooltip("Fractional trail deposition per meter traveled. The solver uses an " +
            "exponential falloff, so 3 deposits about 45% over 0.2 meters without depending on " +
            "simulation frame rate.")]
        public float trailDepositionPerMeter = 3f;
        [Range(0f, 1f)] public float evaporation = 0.015f;
        [Min(0.000001f), Tooltip("Minimum film thickness that participates in flow. Smaller " +
            "injections remain stored so narrow sources can accumulate over multiple fixed steps.")]
        public float minimumVisibleThickness = 0.00002f;

        [Header("Fractal Breakup")]
        [Min(0.0001f)] public float breakupScaleMeters = 0.025f;
        [Range(0f, 1f)] public float breakupStrength = 0.22f;
        [Range(1, 5)] public int breakupOctaves = 3;
        public int breakupSeed = 173;

        [Header("GPU Budget")]
        [Range(64, 1024)] public int simulationResolutionCap = 512;
        [Range(10f, 60f)] public float simulationRate = 24f;
        [Range(1, 8)] public int maximumSubsteps = 4;
        [Range(1f, 30f)] public float surfaceFieldRate = 8f;
        [Range(1f, 30f)] public float compositeRate = 12f;
        public bool reduceRateWhenOffscreen = true;

        [Header("Routing and Fallback")]
        public SurfaceFluidDetachedRoute detachedRoute = SurfaceFluidDetachedRoute.SourceBody;
        public Material fallbackTrailMaterial;
        [Range(4, 64)] public int fallbackMaximumSegments = 24;
        public bool persistAcrossAvatarRebuild;

        private void OnValidate()
        {
            color.a = Mathf.Clamp01(color.a);
            appearanceThicknessScale = Mathf.Max(1f, appearanceThicknessScale);
            appearanceThicknessThreshold = Mathf.Max(0f, appearanceThicknessThreshold);
            depositedTrailOpacityBoost = Mathf.Max(1f, depositedTrailOpacityBoost);
            depositedTrailAlpha = Mathf.Clamp01(depositedTrailAlpha);
            emissionDuration = Mathf.Max(0f, emissionDuration);
            emissionRate = Mathf.Max(0f, emissionRate);
            emissionRadiusMeters = Mathf.Max(0.0001f, emissionRadiusMeters);
            mobileLifetime = Mathf.Max(0.05f, mobileLifetime);
            holdingDuration = Mathf.Max(0f, holdingDuration);
            fadeDuration = Mathf.Max(0.01f, fadeDuration);
            fallSpeedMetersPerSecond = Mathf.Max(0f, fallSpeedMetersPerSecond);
            maximumTravelMeters = Mathf.Max(0.001f, maximumTravelMeters);
            trailDepositionPerMeter = Mathf.Max(0f, trailDepositionPerMeter);
            breakupScaleMeters = Mathf.Max(0.0001f, breakupScaleMeters);
            minimumVisibleThickness = Mathf.Max(0.000001f, minimumVisibleThickness);
            simulationResolutionCap = Mathf.Clamp(simulationResolutionCap, 64, 1024);
            simulationRate = Mathf.Clamp(simulationRate, 10f, 60f);
            surfaceFieldRate = Mathf.Clamp(surfaceFieldRate, 1f, 30f);
            compositeRate = Mathf.Clamp(compositeRate, 1f, 30f);
            maximumSubsteps = Mathf.Clamp(maximumSubsteps, 1, 8);
            fallbackMaximumSegments = Mathf.Clamp(fallbackMaximumSegments, 4, 64);
        }
    }
}
