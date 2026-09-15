using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    [CreateAssetMenu(menuName = "UMA/Hair Cards/Card Profile", fileName = "HairCardProfile")]
    public sealed class HairCardProfileAsset : ScriptableObject
    {
        [SerializeField] private string profileId;
        [SerializeField] private HairCardShape shape = HairCardShape.Ribbon;
        [SerializeField, Range(3, 12)] private int tubeSides = 6;
        [SerializeField, Min(0f)] private float defaultWidth = 0.012f;
        [SerializeField, Min(0f)] private float tipWidth = 0f;
        [SerializeField, Range(2, 64)] private int samplesPerCard = 12;
        [SerializeField] private bool doubleSided = true;
        [SerializeField, Range(1, 4)] private int ribbonSpans = 1;
        [SerializeField, Range(0f, 0.5f)] private float ribbonCamber;
        [SerializeField] private bool adaptiveSampling;
        [SerializeField, Min(0.001f)] private float maximumSegmentLength = 0.012f;
        [SerializeField, Range(2f, 60f)] private float maximumSegmentAngle = 12f;
        [SerializeField] private bool useVertexColorGradient;
        [SerializeField, ColorUsage(true)] private Color rootVertexColor = Color.white;
        [SerializeField, ColorUsage(true)] private Color tipVertexColor = Color.white;
        [SerializeField, Range(0, 63)] private int rootColorSegments;
        [SerializeField] private AnimationCurve widthAlongCard =
            AnimationCurve.Linear(0f, 1f, 1f, 0f);

        public string ProfileId => profileId;
        public HairCardShape Shape => shape;
        public int TubeSides => Mathf.Clamp(tubeSides, 3, 12);
        public float DefaultWidth => Mathf.Max(0f, defaultWidth);
        public float TipWidth => Mathf.Max(0f, tipWidth);
        public int SamplesPerCard => Mathf.Clamp(samplesPerCard, 2, 64);
        public bool DoubleSided => doubleSided;
        public int RibbonSpans => Mathf.Clamp(ribbonSpans, 1, 4);
        public float RibbonCamber => Mathf.Clamp(ribbonCamber, 0f, 0.5f);
        public bool AdaptiveSampling => adaptiveSampling;
        public float MaximumSegmentLength => Mathf.Max(0.001f, maximumSegmentLength);
        public float MaximumSegmentAngle => Mathf.Clamp(maximumSegmentAngle, 2f, 60f);
        public void ConfigureRibbon(int spans, float camber, bool adaptive, float segmentLength = 0.012f, float segmentAngle = 12f)
        {
            ribbonSpans = Mathf.Clamp(spans, 1, 4); ribbonCamber = Mathf.Clamp(camber, 0f, 0.5f);
            adaptiveSampling = adaptive; maximumSegmentLength = Mathf.Max(0.001f, segmentLength);
            maximumSegmentAngle = Mathf.Clamp(segmentAngle, 2f, 60f);
        }
        public int ResolveSampleCount(IReadOnlyList<HairCurvePoint> points, int maximum)
        {
            maximum = Mathf.Clamp(maximum, 2, 64);
            if (!adaptiveSampling || points == null || points.Count < 2) return maximum;
            float length = 0f, turning = 0f; Vector3 previous = Vector3.zero;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 segment = points[i].position - points[i - 1].position;
                length += segment.magnitude;
                if (segment.sqrMagnitude > 1e-12f && previous.sqrMagnitude > 1e-12f) turning += Vector3.Angle(previous, segment);
                if (segment.sqrMagnitude > 1e-12f) previous = segment;
            }
            return Mathf.Clamp(1 + Mathf.Max(Mathf.CeilToInt(length / MaximumSegmentLength), Mathf.CeilToInt(turning / MaximumSegmentAngle)), 2, maximum);
        }
        public bool UseVertexColorGradient => useVertexColorGradient;
        public Color RootVertexColor => rootVertexColor;
        public Color TipVertexColor => tipVertexColor;
        public int RootColorSegments => Mathf.Clamp(rootColorSegments, 0, 63);
        public AnimationCurve WidthAlongCard => widthAlongCard;

        public void ConfigureVertexColors(bool enabled, Color root, Color tip, int solidRootSegments)
        {
            useVertexColorGradient = enabled;
            rootVertexColor = root; tipVertexColor = tip;
            rootColorSegments = Mathf.Clamp(solidRootSegments, 0, 63);
        }

        /// <summary>N solid segments include rows 0 through N. Reserve at least one segment
        /// for the fade at reduced LOD resolutions so the final row always reaches the tip RGBA.</summary>
        public Color EvaluateVertexColor(int row, int sampleCount, Color disabledColor)
        {
            if (!useVertexColorGradient) return disabledColor;
            int segments = Mathf.Max(1, sampleCount - 1);
            int hold = Mathf.Min(RootColorSegments, segments - 1);
            float blend = Mathf.Clamp01((row - hold) / (float)(segments - hold));
            return Color.LerpUnclamped(rootVertexColor, tipVertexColor, blend);
        }

        public float EvaluateWidth(float normalizedLength)
        {
            float t = Mathf.Clamp01(normalizedLength);
            float curve = widthAlongCard != null ? Mathf.Max(0f, widthAlongCard.Evaluate(t)) : 1f - t;
            return Mathf.Lerp(TipWidth, DefaultWidth, curve);
        }

        public void Configure(
            HairCardShape cardShape,
            float rootWidth,
            float endWidth,
            int sampleCount,
            int sideCount = 6,
            bool generateBackfaces = true)
        {
            shape = cardShape;
            defaultWidth = Mathf.Max(0f, rootWidth);
            tipWidth = Mathf.Max(0f, endWidth);
            samplesPerCard = Mathf.Clamp(sampleCount, 2, 64);
            tubeSides = Mathf.Clamp(sideCount, 3, 12);
            doubleSided = generateBackfaces;
            HairStableId.Ensure(ref profileId);
        }

        private void OnValidate()
        {
            HairStableId.Ensure(ref profileId);
            tubeSides = Mathf.Clamp(tubeSides, 3, 12);
            defaultWidth = Mathf.Max(0f, defaultWidth);
            tipWidth = Mathf.Max(0f, tipWidth);
            samplesPerCard = Mathf.Clamp(samplesPerCard, 2, 64);
            rootColorSegments = Mathf.Clamp(rootColorSegments, 0, 63);
            widthAlongCard ??= AnimationCurve.Linear(0f, 1f, 1f, 0f);
        }
    }
}
