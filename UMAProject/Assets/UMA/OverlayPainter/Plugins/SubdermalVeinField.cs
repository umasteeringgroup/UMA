using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    internal static class SubdermalVeinField
    {
        internal static float Sample(Vector3 p, float direction, float density, float width, float branching, int seed, float pixelFootprint = 0)
        {
            float angle = direction * Mathf.Deg2Rad;
            float frequency = Mathf.Max(.01f, density * .15f);
            float footprint = Mathf.Clamp(pixelFootprint * frequency, 0, .04f);
            float along = (p.x * Mathf.Cos(angle) + p.y * Mathf.Sin(angle)) * frequency;
            float across = (-p.x * Mathf.Sin(angle) + p.y * Mathf.Cos(angle)) * frequency;
            across += .17f * Mathf.Sin(p.z * frequency + Random(0, 0, seed) * 6.283185f);
            int lane = Mathf.FloorToInt(across);
            float phase = Random(lane, 0, seed + 13) * 4f;
            int section = Mathf.FloorToInt((along + phase) / 4f);
            float y = along + phase - section * 4f - .15f - .35f * Random(lane, section, seed + 17);
            float length = 2.7f + .7f * Random(lane, section, seed + 23);
            if (y <= 0 || y >= length || Random(lane, section, seed + 29) > .88f) return 0;

            float t = y / length;
            float center = .5f + .21f * Mathf.Sin(along * 1.6f + Random(lane, 0, seed + 31) * 6.283185f)
                + .075f * Mathf.Sin(along * 3.1f + Random(lane, 0, seed + 37) * 6.283185f);
            float radius = .022f * Mathf.Clamp(width, .25f, 3f) * Mathf.Lerp(1f, .22f, t)
                * Mathf.Lerp(.8f, 1.2f, Random(lane, section, seed + 41));
            float rootFade = Mathf.SmoothStep(0, 1, y / .2f);
            float tipFade = Mathf.SmoothStep(0, 1, (length - y) / .25f);
            float value = Profile(across - lane - center, radius, footprint) * rootFade * tipFade;

            // Daughter vessels share the parent's centerline at their attachment.
            // Each side owns a disjoint longitudinal interval; daughters stay inside
            // their parent's corridor, so they form Y junctions rather than X crossings.
            for (int side = -1; side <= 1; side += 2)
            {
                float stagger = side > 0 ? .4f : 0;
                int slot = Mathf.FloorToInt((y - .25f - stagger) / .95f);
                if (slot < 0) continue;
                int key = section * 19 + slot * 2 + (side + 1) / 2;
                if (Random(lane, key, seed + 47) >= branching) continue;
                float start = .25f + stagger + (slot + .12f + .2f * Random(lane, key, seed + 53)) * .95f;
                float span = Mathf.Min(.45f + .18f * Random(lane, key, seed + 59), length - start - .1f);
                float b = (y - start) / Mathf.Max(.001f, span);
                if (b <= 0 || b >= 1 || span <= .05f) continue;
                float reach = .55f + .3f * Random(lane, key, seed + 61);
                float excursion = reach * (b + .16f * Mathf.Sin(b * Mathf.PI));
                float edge = side < 0 ? .04f : .96f;
                float daughter = Mathf.Lerp(center, edge, excursion);
                float daughterRadius = radius * .65f * Mathf.Pow(1 - b, .65f);
                value = Mathf.Max(value, Profile(across - lane - daughter, daughterRadius, footprint)
                    * Mathf.SmoothStep(0, 1, (1 - b) / .15f));

                // A finer terminal fork grows out of a daughter and tapers away.
                float forkStart = .38f + .15f * Random(lane, key, seed + 67);
                float f = (b - forkStart) / .4f;
                if (f > 0 && f < 1 && Random(lane, key, seed + 71) < branching * .65f)
                {
                    float twig = Mathf.Lerp(daughter, edge, .6f * f);
                    value = Mathf.Max(value, Profile(across - lane - twig,
                        daughterRadius * .55f * Mathf.Pow(1 - f, .65f), footprint)
                        * Mathf.SmoothStep(0, 1, (1 - f) / .2f));
                }
            }
            return Mathf.Clamp01(value);
        }

        private static float Profile(float distance, float radius, float footprint)
        {
            // Integrate subpixel vessels approximately, so tapered ends fade instead of stippling.
            float filteredRadius = Mathf.Sqrt(radius * radius + footprint * footprint / 3f);
            float normalized = Mathf.Abs(distance) / Mathf.Max(.00001f, filteredRadius);
            return normalized >= 2 ? 0 : Mathf.Exp(-2f * normalized * normalized) * radius / Mathf.Max(.00001f, filteredRadius);
        }

        private static float Random(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x9e3779b9u ^ (uint)y * 0x85ebca6bu ^ (uint)seed;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (h & 0x00ffffffu) / 16777216f;
            }
        }
    }
}
