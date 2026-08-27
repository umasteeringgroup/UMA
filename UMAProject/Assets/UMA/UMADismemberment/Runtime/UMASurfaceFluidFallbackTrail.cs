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
        private bool stopped;

        internal void Initialize(Vector3 origin, Vector3 normal, UMASurfaceFluidProfile settings,
            Material defaultMaterial)
        {
            profile = settings;
            transform.position = Vector3.zero;
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.widthMultiplier = Mathf.Max(0.0005f, settings.emissionRadiusMeters * 2f);
            line.positionCount = 1;
            line.SetPosition(0, origin + normal.normalized * settings.emissionRadiusMeters);
            line.sharedMaterial = settings.fallbackTrailMaterial != null
                ? settings.fallbackTrailMaterial : defaultMaterial;
            properties = new MaterialPropertyBlock();
            points.Add(line.GetPosition(0));
            tip = points[0];
            velocity = Physics.gravity.sqrMagnitude > 0.000001f
                ? Physics.gravity.normalized * settings.fallSpeedMetersPerSecond
                : Vector3.down * settings.fallSpeedMetersPerSecond;
            ApplyOpacity(1f);
        }

        internal void StopFlow()
        {
            stopped = true;
        }

        internal void FadeNow()
        {
            stopped = true;
            if (fadeStart < 0f) fadeStart = elapsed;
        }

        private void Update()
        {
            if (profile == null || line == null) return;
            float delta = Mathf.Min(Time.deltaTime, 0.1f);
            elapsed += delta;
            bool canGrow = !stopped && elapsed <= profile.emissionDuration &&
                elapsed <= profile.mobileLifetime &&
                points.Count < profile.fallbackMaximumSegments;
            if (canGrow)
            {
                tip += velocity * delta;
                float spacing = Mathf.Max(0.0005f, profile.emissionRadiusMeters * 2f);
                if (Vector3.Distance(points[points.Count - 1], tip) >= spacing)
                {
                    points.Add(tip);
                    line.positionCount = points.Count;
                    line.SetPosition(points.Count - 1, tip);
                }
                if (Vector3.Distance(points[0], tip) >= profile.maximumTravelMeters)
                    stopped = true;
            }
            else if (!stopped && elapsed > profile.emissionDuration)
            {
                stopped = true;
            }

            if (stopped && fadeStart < 0f &&
                elapsed >= profile.emissionDuration + profile.holdingDuration)
                fadeStart = elapsed;
            if (fadeStart < 0f) return;
            float opacity = 1f - Mathf.Clamp01((elapsed - fadeStart) /
                Mathf.Max(0.01f, profile.fadeDuration));
            ApplyOpacity(opacity);
            if (opacity <= 0f) Destroy(gameObject);
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
