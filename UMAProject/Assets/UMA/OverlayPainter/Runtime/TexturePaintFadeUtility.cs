using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Shared path opacity curves: zero is the interior, one is the faded boundary.</summary>
    public static class TexturePaintFadeUtility
    {
        public const float MaximumDistance = 2f;

        public static AnimationCurve DefaultCurve() => AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        public static AnimationCurve CloneCurve(AnimationCurve curve) => curve == null ? null :
            new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };

        public static float Evaluate(AnimationCurve curve, float inwardDistance, float fadeDistance)
        {
            if (fadeDistance <= 0f) return 1f;
            float position = 1f - Mathf.Clamp01(inwardDistance / Mathf.Min(fadeDistance, MaximumDistance));
            return curve == null || curve.length == 0
                ? 1f - Mathf.SmoothStep(0f, 1f, position)
                : Mathf.Clamp01(curve.Evaluate(position));
        }

        public static int CurveSignature(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)curve.preWrapMode;
                hash = hash * 31 + (int)curve.postWrapMode;
                foreach (Keyframe key in curve.keys)
                {
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                    hash = hash * 31 + key.inWeight.GetHashCode();
                    hash = hash * 31 + key.outWeight.GetHashCode();
                    hash = hash * 31 + (int)key.weightedMode;
                }
                return hash;
            }
        }
    }
}
