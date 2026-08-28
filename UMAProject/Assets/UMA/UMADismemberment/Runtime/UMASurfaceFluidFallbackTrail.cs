using System.Collections.Generic;
using UnityEngine;

namespace UMA.Dismemberment
{
    /// <summary>
    /// Bounded non-compute fallback. It never edits an UMA texture or asks UMA to rebuild.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class UMASurfaceFluidFallbackTrail : MonoBehaviour
    {
        private readonly List<Vector3> points = new List<Vector3>(32);
        private LineRenderer line;
        private MaterialPropertyBlock properties;
        private UMASurfaceFluidProfile profile;
        private float elapsed;
        private float fadeStart = -1f;
        private Vector3 velocity;
        private Vector3 tip;
        private float sourceRadius;
        private bool stopped;
        private float stoppedAt = -1f;

        internal void Initialize(Vector3 origin, Vector3 normal, UMASurfaceFluidProfile settings,
            Material defaultMaterial, float speedMultiplier = 1f,
            float sizeMultiplier = 1f)
        {
            profile = settings;
            speedMultiplier = Mathf.Max(0.05f, speedMultiplier);
            sizeMultiplier = Mathf.Max(0.05f, sizeMultiplier);
            sourceRadius = settings.emissionRadiusMeters * sizeMultiplier;
            line = gameObject.AddComponent<LineRenderer>();
            // Fallback geometry follows its cut/decal anchor. New growth is converted from
            // world gravity each frame, so an animated or ragdolled character cannot leave a
            // permanent blood line suspended at the original pose.
            line.useWorldSpace = false;
            line.loop = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.widthMultiplier = Mathf.Max(0.0005f, sourceRadius * 2f);
            line.positionCount = 1;
            line.SetPosition(0, transform.InverseTransformPoint(
                origin + normal.normalized * sourceRadius));
            line.sharedMaterial = settings.fallbackTrailMaterial != null
                ? settings.fallbackTrailMaterial : defaultMaterial;
            properties = new MaterialPropertyBlock();
            points.Add(line.GetPosition(0));
            tip = points[0];
            velocity = Physics.gravity.sqrMagnitude > 0.000001f
                ? Physics.gravity.normalized * settings.fallSpeedMetersPerSecond * speedMultiplier
                : Vector3.down * settings.fallSpeedMetersPerSecond * speedMultiplier;
            ApplyOpacity(1f);
        }

        internal void StopFlow()
        {
            if (stopped) return;
            stopped = true;
            stoppedAt = elapsed;
        }

        internal void FadeNow()
        {
            StopFlow();
            if (fadeStart < 0f) fadeStart = elapsed;
        }

        private void Update()
        {
            if (profile == null || line == null) return;
            float delta = Mathf.Min(Time.deltaTime, 0.1f);
            elapsed += delta;
            float fadeDuration = ResolveFallbackFadeDuration(profile);
            float maximumLifetime = ResolveFallbackMaximumLifetime(profile, fadeDuration);
            if (elapsed >= maximumLifetime)
            {
                Destroy(gameObject);
                return;
            }
            bool canGrow = !stopped && elapsed <= profile.emissionDuration &&
                elapsed <= profile.mobileLifetime &&
                points.Count < profile.fallbackMaximumSegments;
            if (canGrow)
            {
                tip += transform.InverseTransformVector(velocity) * delta;
                float spacing = Mathf.Max(0.0005f, sourceRadius * 2f);
                if (Vector3.Distance(transform.TransformPoint(points[points.Count - 1]),
                    transform.TransformPoint(tip)) >= spacing)
                {
                    points.Add(tip);
                    line.positionCount = points.Count;
                    line.SetPosition(points.Count - 1, tip);
                }
                if (Vector3.Distance(transform.TransformPoint(points[0]),
                    transform.TransformPoint(tip)) >= profile.maximumTravelMeters)
                    StopFlow();
            }
            else if (!stopped)
            {
                StopFlow();
            }

            if (stopped && fadeStart < 0f &&
                elapsed >= stoppedAt + ResolveFallbackHoldingDuration(profile))
                fadeStart = elapsed;
            float forcedFadeStart = maximumLifetime - fadeDuration;
            if (fadeStart < 0f && elapsed >= forcedFadeStart)
                fadeStart = forcedFadeStart;
            if (fadeStart < 0f) return;
            float opacity = 1f - Mathf.Clamp01((elapsed - fadeStart) /
                fadeDuration);
            ApplyOpacity(opacity);
            if (opacity <= 0f) Destroy(gameObject);
        }

        private static float ResolveFallbackHoldingDuration(UMASurfaceFluidProfile settings)
        {
            float configured = settings.fallbackHoldingDuration >= 0f
                ? settings.fallbackHoldingDuration : 0.25f;
            return Mathf.Min(settings.holdingDuration, configured);
        }

        private static float ResolveFallbackFadeDuration(UMASurfaceFluidProfile settings)
        {
            float configured = settings.fallbackFadeDuration > 0f
                ? settings.fallbackFadeDuration : 1.25f;
            return Mathf.Max(0.01f, Mathf.Min(settings.fadeDuration, configured));
        }

        private static float ResolveFallbackMaximumLifetime(UMASurfaceFluidProfile settings,
            float fadeDuration)
        {
            float configured = settings.fallbackMaximumLifetime > 0f
                ? settings.fallbackMaximumLifetime : 8f;
            return Mathf.Max(fadeDuration + 0.05f, configured);
        }

        private void ApplyOpacity(float opacity)
        {
            if (line == null) return;
            Color color = profile != null ? profile.color : Color.red;
            color.a *= Mathf.Clamp01(opacity);
            properties.Clear();
            properties.SetColor("_Color", color);
            properties.SetColor("_BaseColor", color);
            line.SetPropertyBlock(properties);
        }
    }
}
