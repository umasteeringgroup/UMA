using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The body measurements the garment generator drafts from, in centimetres. Heights are measured down from
    /// the top of the shoulders (where torso pieces are placed from); girths round the body. Read from the body in
    /// the designer, or Human Female 3.0's (measured 2026-11-23) when there is none.
    /// </summary>
    public sealed class GarmentBody
    {
        public float chest = 100.1f, waist = 67.2f, hip = 117.1f, neck = 41f, head = 63.6f, thigh = 67.9f, upperArm = 30.4f;
        /// <summary>Heights below the top of the shoulders (cm).</summary>
        public float chestY = 18.7f, waistY = 37f, hipY = 67.7f, crotchY = 69.7f, kneeY = 111.6f, ankleY = 158f, floorY = 168f;
        /// <summary>The shoulder joint's distance from the body's middle (cm).</summary>
        public float shoulderX = 17.5f;
        /// <summary>From the shoulder joint to the wrist (cm).</summary>
        public float armLength = 56.9f;
        /// <summary>From the crotch to the ankle (cm), as the pattern's sizes measure it.</summary>
        public float inseam = 88.2f;
        /// <summary>From the base of the neck to the top of the head (cm).</summary>
        public float headHeight = 29.8f;
        /// <summary>How far the top of the head is above the top of the shoulders (cm): where head pieces are placed from.</summary>
        public float headTopAbove = 30.9f;
        /// <summary>True when read from a body, false for the built-in measurements.</summary>
        public bool measured;

        /// <summary>Girths round the torso every <see cref="Step"/> cm from the chest down to the crotch; null for the built-in body.</summary>
        private float[] torso;
        private float torsoStart;
        /// <summary>Girths round the arm every <see cref="Step"/> cm from the shoulder joint; null when not known.</summary>
        private float[] arm;
        private const float Step = 2f;

        /// <summary>Human Female 3.0's measurements.</summary>
        public static GarmentBody Default => new GarmentBody();

        /// <summary>Reads a body; the built-in measurements for anything it does not give.</summary>
        public static GarmentBody From(BodyReference body)
        {
            var g = new GarmentBody();
            if (body == null || body.Landmarks == null || !body.Landmarks.valid) return g;
            BodyLandmarks m = body.Landmarks;
            float top = m.shoulderTopY;
            float Below(float y) => (top - y) * 100f;
            g.measured = true;
            if (m.chestCm > 1f) g.chest = m.chestCm;
            if (m.waistCm > 1f) g.waist = m.waistCm;
            if (m.hipCm > 1f) g.hip = m.hipCm;
            g.chestY = Below(m.chestY);
            g.waistY = Below(m.waistY);
            g.hipY = Below(m.hipY);
            if (m.neckCm > 1f) g.neck = m.neckCm;
            if (m.headCm > 1f) g.head = m.headCm;
            if (m.thighCm > 1f) g.thigh = m.thighCm;
            if (m.upperArmCm > 1f) g.upperArm = m.upperArmCm;
            if (m.limbs)
            {
                g.crotchY = Below(m.crotchY);
                float ankle = Below(0.5f * (m.leftAnkle.y + m.rightAnkle.y));
                float hipJoint = Below(0.5f * (m.leftHip.y + m.rightHip.y));
                if (ankle > g.crotchY + 10f)
                {
                    g.ankleY = ankle;
                    // The knee a little below halfway from the hip joint to the ankle (the thigh bone is the longer).
                    g.kneeY = hipJoint + 0.52f * (ankle - hipJoint);
                }
                g.floorY = Mathf.Max(g.ankleY + 4f, Below(m.headTopY - m.heightCm * 0.01f));
                g.shoulderX = 0.5f * Mathf.Abs(m.leftShoulder.x - m.rightShoulder.x) * 100f;
                if (m.armLengthCm > 1f) g.armLength = m.armLengthCm;
                g.inseam = m.inseamCm > 1f ? m.inseamCm : g.ankleY - g.crotchY;
                g.headHeight = Mathf.Max(15f, (m.headTopY - m.neckY) * 100f);
                g.headTopAbove = Mathf.Max(10f, (m.headTopY - m.shoulderTopY) * 100f);
                // Round the arm along its length, where the body gave the arm's profile.
                if (m.leftArmProfile != null && m.leftArmProfile.Length > 2)
                {
                    int n = Mathf.CeilToInt(g.armLength / Step) + 1;
                    g.arm = new float[n];
                    for (int i = 0; i < n; i++) g.arm[i] = 2f * Mathf.PI * m.ArmSurfaceRadius(true, i * Step * 0.01f) * 100f;
                }
            }
            // Round the torso, chest to crotch, measured: waists and hips are not straight lines between three girths.
            g.torsoStart = g.chestY;
            int count = Mathf.Max(2, Mathf.FloorToInt((g.crotchY - 1f - g.chestY) / Step) + 1);
            g.torso = new float[count];
            for (int i = 0; i < count; i++)
            {
                float c = body.Circumference(top - (g.chestY + i * Step) * 0.01f, out _) * 100f;
                g.torso[i] = c > 10f ? c : g.ShapeGirth(g.chestY + i * Step);
            }
            return g;
        }

        /// <summary>Round the torso at a height (cm below the shoulders), arms left out. Below the hips it stays the hips'.</summary>
        public float TorsoGirth(float y)
        {
            if (y >= hipY) return Mathf.Max(hip, torso != null ? Sample(torso, torsoStart, Mathf.Min(y, hipY)) : hip);
            if (torso != null && y >= torsoStart) return Sample(torso, torsoStart, y);
            return ShapeGirth(y);
        }

        /// <summary>
        /// The narrowest the torso is from a height down to the hips (cm below the shoulders): what a top edge has to be
        /// no wider than to stay up. Bodies that narrow below the waist (a man's, under the belly) let a band cut to the
        /// waist slide down to the hips: skirts on Human Male 3.0 dropped 15-20 cm and a pleated one fell off.
        /// </summary>
        public float NarrowestBelow(float from)
        {
            float narrowest = TorsoGirth(from);
            for (float y = from + 1f; y <= hipY; y += 1f) narrowest = Mathf.Min(narrowest, TorsoGirth(y));
            return narrowest;
        }

        /// <summary>The widest the torso is between two heights (cm below the shoulders).</summary>
        public float WidestTorso(float from, float to)
        {
            if (to < from) (from, to) = (to, from);
            float widest = 0f;
            for (float y = from; y <= to + 0.01f; y += 1f) widest = Mathf.Max(widest, TorsoGirth(y));
            return widest;
        }

        private float ShapeGirth(float y)
        {
            if (y <= chestY) return chest;
            if (y <= waistY) return Mathf.Lerp(chest, waist, (y - chestY) / Mathf.Max(1f, waistY - chestY));
            if (y <= hipY) return Mathf.Lerp(waist, hip, Mathf.SmoothStep(0f, 1f, (y - waistY) / Mathf.Max(1f, hipY - waistY)));
            return hip;
        }

        /// <summary>Round the arm at a distance from the shoulder joint (cm).</summary>
        public float ArmGirth(float along)
        {
            if (arm != null) return Sample(arm, 0f, along);
            // The upper arm at a quarter of the way down, the wrist a little over half of it.
            float t = Mathf.Clamp01((along - 0.25f * armLength) / (0.75f * armLength));
            return Mathf.Lerp(upperArm, upperArm * 0.55f, t);
        }

        /// <summary>Round one leg at a height (cm below the shoulders): the thigh at the crotch, narrowing to the knee and the ankle.</summary>
        public float LegGirth(float y)
        {
            float knee = thigh * 0.62f, ankle = thigh * 0.36f;
            if (y <= crotchY) return thigh;
            if (y <= kneeY) return Mathf.Lerp(thigh, knee, (y - crotchY) / Mathf.Max(1f, kneeY - crotchY));
            float calf = knee * 0.98f;
            float mid = 0.5f * (kneeY + ankleY);
            if (y <= mid) return Mathf.Lerp(knee, calf, (y - kneeY) / Mathf.Max(1f, mid - kneeY));
            return Mathf.Lerp(calf, ankle, Mathf.Clamp01((y - mid) / Mathf.Max(1f, ankleY - mid)));
        }

        private static float Sample(float[] table, float start, float at)
        {
            float f = Mathf.Clamp((at - start) / Step, 0f, table.Length - 1);
            int i = Mathf.Min(table.Length - 2, (int)f);
            return i < 0 ? table[0] : Mathf.Lerp(table[i], table[i + 1], f - i);
        }
    }
}
