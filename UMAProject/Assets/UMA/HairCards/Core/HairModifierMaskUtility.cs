using UnityEngine;

namespace UMA.HairCards
{
    public static class HairModifierMaskUtility
    {
        public static float Evaluate(HairModifierSettings modifier, HairEvaluatedCurve curve,
            HairGroup group, HairSurfaceFields fields)
        {
            float value = 1f;
            if (!string.IsNullOrEmpty(modifier.maskMapId))
            {
                var map = group?.maps.Find(m => m != null && m.Id == modifier.maskMapId);
                // A missing explicitly assigned mask must not unexpectedly affect the whole groom.
                if (map == null || fields == null) return 0f;
                value *= Mathf.Clamp01(fields.Sample(map, curve.rootAnchor, 0f));
            }
            var mask = modifier.mask;
            if (mask == null) return value;
            if (mask.useLength) value *= Ramp(mask.lengthRange, curve.Length);
            if (mask.useHairline) value *= Ramp(mask.hairlineRange, curve.hairlineDistance);
            if (mask.randomVariation > 0f)
            {
                var strand = new HairDeterministicRandom(modifier.seed ^ curve.seed);
                var parent = new HairDeterministicRandom(modifier.seed ^ curve.clumpSeed);
                value *= Mathf.Lerp(1f, Mathf.Lerp(strand.Next01(), parent.Next01(), mask.parentCoherence), mask.randomVariation);
            }
            value = mask.remap != null ? mask.remap.Evaluate(value) : value;
            if (mask.invert) value = 1f - value;
            return float.IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        }
        private static float Ramp(Vector2 range, float value) => Mathf.Approximately(range.x, range.y)
            ? (value >= range.y ? 1f : 0f) : Mathf.Clamp01((value - range.x) / (range.y - range.x));
    }
}
