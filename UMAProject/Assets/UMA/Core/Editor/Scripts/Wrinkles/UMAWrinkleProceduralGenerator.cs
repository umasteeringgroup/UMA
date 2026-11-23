using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace UMA.Editors
{
    /// <summary>
    /// Face landmarks for the procedural wrinkle generator, as seen from the front: X across the face in millimetres
    /// (positive on the character's right), Z up in millimetres, in the head slot's mesh space. Only the right side is
    /// given; the left is its mirror image. The generator finds the surface point under each landmark itself.
    /// </summary>
    public sealed class UMAWrinkleFaceLandmarks
    {
        /// <summary>Mesh-space axes. UMA 3 slots are Z up with the face looking along -Y.</summary>
        public Vector3 Right = new Vector3(1, 0, 0), Up = new Vector3(0, 0, 1), Forward = new Vector3(0, -1, 0);
        /// <summary>Position along Forward (mesh units) of the vertical axis the face is unwrapped around.</summary>
        public float AxisForward = 0.005f;
        /// <summary>Radius of the unwrapping cylinder, mesh units. Distances across the face are kept true near this depth.</summary>
        public float WrapRadius = 0.09f;

        public Vector2 EyeCenter = new Vector2(35, 1857);
        public Vector2 EyeInnerCorner = new Vector2(20, 1857);
        public Vector2 EyeOuterCorner = new Vector2(50, 1857);
        /// <summary>Half width and half height of the eye opening, mm.</summary>
        public Vector2 EyeRadius = new Vector2(16, 7);
        /// <summary>Highest point of the brow, above the eye.</summary>
        public Vector2 BrowPeak = new Vector2(33, 1873);
        public float ForeheadTop = 1920;
        public Vector2 NoseRoot = new Vector2(0, 1859);
        /// <summary>Top of the crease where the nostril wing meets the cheek.</summary>
        public Vector2 NoseWing = new Vector2(17, 1813);
        public float NoseBase = 1806;
        public Vector2 MouthCorner = new Vector2(25, 1777);
        public float UpperLipBorder = 1788, LowerLipBorder = 1765, MouthLine = 1776;
        public Vector2 ChinCenter = new Vector2(0, 1748);

        /// <summary>Landmarks measured on the UMA 3 head (UMA30_Body_UDIM1001), female and male.</summary>
        public static UMAWrinkleFaceLandmarks UMA3Head() { return new UMAWrinkleFaceLandmarks(); }
    }

    /// <summary>Output of <see cref="UMAWrinkleProceduralGenerator"/>. Row 0 is the top of the texture.</summary>
    public sealed class UMAWrinkleBakeResult
    {
        public int Size;
        /// <summary>RGBA per texel, 0..1: RG wrinkle normal XY (0.5 flat), B crease depth, A flush.</summary>
        public float[] WrinkleMap;
        /// <summary>Indexed by zone id; null for zones the generator does not make (neck).</summary>
        public float[][] ZoneMasks;
        /// <summary>Height of the wrinkle surface in millimetres, for previews.</summary>
        public float[] HeightMM;
    }

    /// <summary>
    /// Makes expression wrinkles procedurally for a head: a wrinkle map (normal, crease depth, flush) and one mask per
    /// <see cref="UMAWrinkleZone"/>. Every wrinkle is a curve on the face, laid out from the landmarks and unwrapped
    /// around a vertical axis so spacing holds on the sides of the face. The texture is computed from 3D surface
    /// position, so it is continuous across UV seams, and its normals are taken along each texel's own tangent frame.
    /// <para>
    /// The cross-section of a wrinkle is a narrow valley between broad, slightly raised shoulders that hold the same
    /// volume (skin folds, it does not vanish). Depth and width wander along each line, lines break, taper and fork,
    /// and the two sides of the face use different random streams, so no two lines are alike.
    /// </para>
    /// Pure C#: no UnityEngine types.
    /// </summary>
    public static class UMAWrinkleProceduralGenerator
    {
        /// <summary>Crease depth stored as 1 in the wrinkle map's blue channel, mm.</summary>
        public const float CreaseReferenceMM = 0.5f;
        /// <summary>Largest slope the wrinkle normal can store before it is clamped.</summary>
        public const float MaxSlope = 3.5f;

        // One wrinkle: a polyline on the unwrapped face (mm) and its cross-section.
        sealed class Curve
        {
            public Vector2[] Points;
            public float[] Along;       // arc length at each point, mm
            public float Length;
            public float Depth = 0.4f;  // mm
            public float Width = 0.5f;  // valley half width (Gaussian sigma), mm
            public float Shoulder = 3f; // shoulder width as a multiple of Width
            public float Roll = -1f;    // height of the rolls beside the valley relative to Depth; below 0 keeps the volume (1 / Shoulder)
            public float Bulge;         // raised roll on one side, mm (nasolabial and mouth-corner folds)
            public float BulgeOffset, BulgeWidth = 1;
            public float BulgeSide = 1; // +1: on the left of the curve's direction
            public float TaperStart = 4, TaperEnd = 4; // mm
            public float Wander = 0.35f;   // relative depth variation along the line
            public float Breaks;           // 0 none .. 1 mostly broken
            public float Flush = 1;
            public int Seed;
            public int Zone;
            public int PairZone;           // a zone that touches Zone; the line shows in either (forehead lines)
            public float Reach;           // influence radius, mm
            public Vector2 Min, Max;

            public void Finish()
            {
                Along = new float[Points.Length];
                for (int i = 1; i < Points.Length; i++) Along[i] = Along[i - 1] + Vector2.Distance(Points[i], Points[i - 1]);
                Length = Along[Along.Length - 1];
                Reach = Math.Max(Width * Shoulder * 2.6f, Bulge > 0 ? Math.Abs(BulgeOffset) + BulgeWidth * 2.6f : 0) + 0.5f;
                Min = new Vector2(float.MaxValue); Max = new Vector2(float.MinValue);
                foreach (var p in Points) { Min = Vector2.Min(Min, p); Max = Vector2.Max(Max, p); }
                Min -= new Vector2(Reach); Max += new Vector2(Reach);
            }
        }

        struct Contribution { public float Height, Crease, Flush; }

        sealed class Context
        {
            public UMAWrinkleFaceLandmarks L;
            public UMAWrinkleSurfaceMap Map;
            public List<Curve> Curves = new List<Curve>();
            // landmarks resolved onto the unwrapped face, right side
            public Vector2 Eye, EyeInner, EyeOuter, Brow, NoseRoot, NoseWing, Mouth, Chin;
            // acceleration grid over the unwrapped face
            public float GridMin = -140, GridCell = 3f; public int GridN;
            // per cell: (curve index << 16 | segment index + 1), sorted so the segments of one curve are together
            public long[][] Grid;
        }

        public static UMAWrinkleBakeResult Generate(UMAWrinkleSurfaceMap map, UMAWrinkleFaceLandmarks landmarks, int seed, int supersample = 2)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var ctx = new Context { L = landmarks ?? UMAWrinkleFaceLandmarks.UMA3Head(), Map = map };
            ResolveLandmarks(ctx);
            var rng = new Random(seed);
            BuildCurves(ctx, rng);
            BuildGrid(ctx);

            int size = map.Size, n = size * size;
            var result = new UMAWrinkleBakeResult { Size = size, WrinkleMap = new float[n * 4], HeightMM = new float[n], ZoneMasks = new float[UMAWrinkleZones.IdLevels][] };
            for (int z = 1; z <= 13; z++) result.ZoneMasks[z] = new float[n];
            int ss = Math.Max(1, supersample);
            Parallel.For(0, size, y =>
            {
                var maskTemp = new float[UMAWrinkleZones.IdLevels];
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    result.WrinkleMap[i * 4] = 0.5f; result.WrinkleMap[i * 4 + 1] = 0.5f;
                    if (!map.Covered[i]) continue;
                    Vector3 p = map.Position[i], nrm = map.Normal[i];
                    Vector2 f = Unwrap(ctx.L, p);
                    float facing = Vector3.Dot(nrm, ctx.L.Forward);
                    Masks(ctx, f, facing, maskTemp);
                    bool any = false;
                    for (int z = 1; z <= 13; z++) { result.ZoneMasks[z][i] = maskTemp[z]; any |= maskTemp[z] > 0; }
                    if (!any) continue;
                    // Supersampled slope along the tangent and bitangent.
                    Vector2 slope = Vector2.Zero; float h = 0, crease = 0, flush = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float ox = (sx + 0.5f) / ss - 0.5f, oy = (sy + 0.5f) / ss - 0.5f;
                            Vector3 q = p + map.TexelRight[i] * ox + map.TexelDown[i] * oy;
                            var c = Evaluate(ctx, Unwrap(ctx.L, q), maskTemp);
                            const float step = 0.00002f; // 0.02 mm
                            float ht = Evaluate(ctx, Unwrap(ctx.L, q + map.Tangent[i] * step), maskTemp).Height - c.Height;
                            float hb = Evaluate(ctx, Unwrap(ctx.L, q + map.Bitangent[i] * step), maskTemp).Height - c.Height;
                            slope += new Vector2(ht, hb) / (step * 1000f);
                            h += c.Height; crease += c.Crease; flush += c.Flush;
                        }
                    float inv = 1f / (ss * ss);
                    slope *= inv; h *= inv; crease *= inv; flush *= inv;
                    slope = Vector2.Clamp(slope, new Vector2(-MaxSlope), new Vector2(MaxSlope));
                    var normal = Vector3.Normalize(new Vector3(-slope.X, -slope.Y, 1));
                    result.WrinkleMap[i * 4] = normal.X * 0.5f + 0.5f;
                    result.WrinkleMap[i * 4 + 1] = normal.Y * 0.5f + 0.5f;
                    result.WrinkleMap[i * 4 + 2] = Clamp01(crease / CreaseReferenceMM);
                    result.WrinkleMap[i * 4 + 3] = Clamp01(flush);
                    result.HeightMM[i] = h;
                }
            });
            // Padding texels copy their nearest covered texel.
            for (int i = 0; i < n; i++)
            {
                int s = map.Source[i];
                if (map.Covered[i] || s < 0) continue;
                for (int k = 0; k < 4; k++) result.WrinkleMap[i * 4 + k] = result.WrinkleMap[s * 4 + k];
                result.HeightMM[i] = result.HeightMM[s];
                for (int z = 1; z <= 13; z++) result.ZoneMasks[z][i] = result.ZoneMasks[z][s];
            }
            return result;
        }

        // ---- Face coordinates -------------------------------------------------------------------------------

        /// <summary>Unwraps a mesh-space point onto the face: X across (mm, character's right positive), Y up (mm).</summary>
        static Vector2 Unwrap(UMAWrinkleFaceLandmarks l, Vector3 p)
        {
            float across = Vector3.Dot(p, l.Right);
            float depth = Vector3.Dot(p, l.Forward) - l.AxisForward; // distance in front of the axis
            float s = l.WrapRadius * (float)Math.Atan2(across, depth);
            return new Vector2(s * 1000f, Vector3.Dot(p, l.Up) * 1000f);
        }

        static void ResolveLandmarks(Context ctx)
        {
            ctx.Eye = Resolve(ctx, ctx.L.EyeCenter);
            ctx.EyeInner = Resolve(ctx, ctx.L.EyeInnerCorner);
            ctx.EyeOuter = Resolve(ctx, ctx.L.EyeOuterCorner);
            ctx.Brow = Resolve(ctx, ctx.L.BrowPeak);
            ctx.NoseRoot = Resolve(ctx, ctx.L.NoseRoot);
            ctx.NoseWing = Resolve(ctx, ctx.L.NoseWing);
            ctx.Mouth = Resolve(ctx, ctx.L.MouthCorner);
            ctx.Chin = Resolve(ctx, ctx.L.ChinCenter);
        }

        // The front-most surface point under a front-view landmark, unwrapped.
        static Vector2 Resolve(Context ctx, Vector2 frontMM)
        {
            var map = ctx.Map; var l = ctx.L;
            float best = float.MinValue; Vector2 found = frontMM; float bestMiss = float.MaxValue; Vector2 nearest = frontMM;
            for (int i = 0; i < map.Covered.Length; i++)
            {
                if (!map.Covered[i]) continue;
                var p = map.Position[i];
                float dx = Vector3.Dot(p, l.Right) * 1000f - frontMM.X, dz = Vector3.Dot(p, l.Up) * 1000f - frontMM.Y;
                float miss = dx * dx + dz * dz;
                if (miss < bestMiss) { bestMiss = miss; nearest = Unwrap(l, p); }
                if (miss > 1.5f * 1.5f) continue;
                float front = Vector3.Dot(p, l.Forward);
                if (front > best) { best = front; found = Unwrap(l, p); }
            }
            return best > float.MinValue ? found : nearest;
        }

        // ---- Masks --------------------------------------------------------------------------------------------

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static float Smooth(float a, float b, float x) { float t = Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        // 1 inside, falling to exactly 0 at the ellipse edge; feather is the fraction of the radius used for the fall.
        static float Ellipse(Vector2 p, Vector2 c, Vector2 r, float feather, float angleDeg = 0)
        {
            var d = p - c;
            if (angleDeg != 0)
            {
                float a = angleDeg * (float)Math.PI / 180f, ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                d = new Vector2(d.X * ca + d.Y * sa, -d.X * sa + d.Y * ca);
            }
            float e = (float)Math.Sqrt((d.X / r.X) * (d.X / r.X) + (d.Y / r.Y) * (d.Y / r.Y));
            return 1 - Smooth(1 - feather, 1, e);
        }

        /// <summary>Strength of the forehead zones where they meet at the midline; full strength 6 mm to each side.</summary>
        const float ForeheadMidlineStrength = 0.65f;

        static void Masks(Context ctx, Vector2 f, float facing, float[] m)
        {
            Array.Clear(m, 0, m.Length);
            float front = Smooth(-0.25f, 0.1f, facing);
            if (front <= 0) return;
            var l = ctx.L;
            float forehead = ForeheadMask(ctx, f);
            // The halves touch at the midline (the packer lets this pair share a layer without a gap, see
            // UMAWrinkleControlMapPacker.TouchingPairs) and fade a little towards it, so with both brows raised each
            // line runs straight through, slightly weaker in the middle. The glabella overlaps both on the other layer.
            float centre = ForeheadMidlineStrength + (1f - ForeheadMidlineStrength) * Smooth(0f, 6f, Math.Abs(f.X));
            m[(int)UMAWrinkleZone.ForeheadRight] = f.X >= 0 ? forehead * centre : 0f;
            m[(int)UMAWrinkleZone.ForeheadLeft] = f.X < 0 ? forehead * centre : 0f;
            m[(int)UMAWrinkleZone.Glabella] = Ellipse(f, new Vector2(0, ctx.NoseRoot.Y + 22f), new Vector2(23, 15f), 0.5f);
            m[(int)UMAWrinkleZone.NoseRoot] = Ellipse(f, new Vector2(0, ctx.NoseRoot.Y + 4.5f), new Vector2(13, 7), 0.55f);
            m[(int)UMAWrinkleZone.BunnyLines] = TubeMask(ctx, f, (int)UMAWrinkleZone.BunnyLines, 4f, 4f, false);
            m[(int)UMAWrinkleZone.CrowsFeetRight] = CrowMask(ctx, f, 1);
            m[(int)UMAWrinkleZone.CrowsFeetLeft] = CrowMask(ctx, f, -1);
            m[(int)UMAWrinkleZone.NasolabialRight] = TubeMask(ctx, f, (int)UMAWrinkleZone.NasolabialRight, 17, 4.5f);
            m[(int)UMAWrinkleZone.NasolabialLeft] = TubeMask(ctx, f, (int)UMAWrinkleZone.NasolabialLeft, 17, 4.5f);
            m[(int)UMAWrinkleZone.MouthCornerRight] = TubeMask(ctx, f, (int)UMAWrinkleZone.MouthCornerRight, 8, 4f);
            m[(int)UMAWrinkleZone.MouthCornerLeft] = TubeMask(ctx, f, (int)UMAWrinkleZone.MouthCornerLeft, 8, 4f);
            float lipCenter = (l.UpperLipBorder + l.LowerLipBorder) * 0.5f;
            m[(int)UMAWrinkleZone.LipLines] = Ellipse(f, new Vector2(0, lipCenter + 4), new Vector2(17.5f, 25), 0.35f)
                * Smooth(l.NoseBase + 1, l.NoseBase - 3, f.Y);
            m[(int)UMAWrinkleZone.Chin] = Ellipse(f, ctx.Chin, new Vector2(17, 12), 0.5f);
            for (int z = 1; z < m.Length; z++) m[z] *= front;
        }

        static float BrowHeight(Context ctx, float s)
        {
            float a = Math.Abs(s) - ctx.Brow.X;
            return ctx.Brow.Y - 0.0045f * a * a;
        }

        static float ForeheadMask(Context ctx, Vector2 f)
        {
            // just above the brow, lifted in the middle where the glabella lines take over
            float bottom = Math.Max(BrowHeight(ctx, f.X) - 3f, ctx.NoseRoot.Y + 14f + 14f * (float)Math.Exp(-f.X * f.X / (20f * 20f)));
            float v = Smooth(bottom, bottom + 5f, f.Y) * Smooth(ctx.L.ForeheadTop + 2, ctx.L.ForeheadTop - 7, f.Y);
            return v * Smooth(76, 50, Math.Abs(f.X)) * Ellipse(f, new Vector2(0, ctx.L.ForeheadTop - 28f), new Vector2(80, 44), 0.45f);
        }

        static float CrowMask(Context ctx, Vector2 f, int side)
        {
            var eye = new Vector2(ctx.Eye.X * side, ctx.Eye.Y);
            var corner = new Vector2((ctx.EyeOuter.X + 4) * side, ctx.EyeOuter.Y);
            var d = f - corner;
            float r = d.Length();
            float angle = (float)(Math.Atan2(d.Y, d.X * side) * 180 / Math.PI);
            float fan = Smooth(30, 24, r) * Smooth(-62, -48, angle) * Smooth(50, 38, angle) * Smooth(1.5f, 4f, r + Math.Max(0, d.X * side));
            // folds under the lower lid
            float under = Ellipse(f, eye + new Vector2(2 * side, -4), new Vector2(21, 12), 0.4f) * Smooth(eye.Y - 2.5f, eye.Y - 6.5f, f.Y)
                * Smooth(ctx.Eye.X - 13, ctx.Eye.X - 8, f.X * side);
            float eyeHole = 1 - Ellipse(f, eye, ctx.L.EyeRadius + new Vector2(1.5f, 1.5f), 0.3f);
            return Math.Max(fan, under) * eyeHole;
        }

        static float TubeMask(Context ctx, Vector2 f, int zone, float lateral, float medial, bool foldsOnly = true)
        {
            float best = 0;
            foreach (var c in ctx.Curves)
            {
                if (c.Zone != zone || (foldsOnly && c.Bulge <= 0)) continue;
                if (f.X < c.Min.X - lateral || f.X > c.Max.X + lateral || f.Y < c.Min.Y - lateral || f.Y > c.Max.Y + lateral) continue;
                Nearest(c, f, out float d, out float along);
                float signedLateral = d * c.BulgeSide;
                float reach = signedLateral >= 0 ? lateral : medial;
                float v = Smooth(reach, reach * 0.35f, Math.Abs(d)) * Smooth(-3, 4, along) * Smooth(c.Length + 4, c.Length - 4, along);
                best = Math.Max(best, v);
            }
            return best;
        }

        // ---- Curves -------------------------------------------------------------------------------------------

        static float Rand(Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        static Curve Line(Random r, IList<Vector2> pts, float wobbleMM, float wobbleScaleMM)
        {
            // Resample to 1 mm steps and let the line wander sideways a little.
            var dense = new List<Vector2>();
            int seed = r.Next();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                int steps = Math.Max(1, (int)Math.Ceiling(len / 1f));
                for (int k = 0; k < steps; k++) dense.Add(Vector2.Lerp(pts[i], pts[i + 1], k / (float)steps));
            }
            dense.Add(pts[pts.Count - 1]);
            if (dense.Count > 2 && wobbleMM > 0)
            {
                float along = 0;
                var outPts = new Vector2[dense.Count];
                for (int i = 0; i < dense.Count; i++)
                {
                    if (i > 0) along += Vector2.Distance(dense[i], dense[i - 1]);
                    var dir = dense[Math.Min(i + 1, dense.Count - 1)] - dense[Math.Max(i - 1, 0)];
                    dir = dir.LengthSquared() > 0 ? Vector2.Normalize(dir) : Vector2.UnitX;
                    var normal = new Vector2(-dir.Y, dir.X);
                    float w = (Noise1(along / wobbleScaleMM, seed) * 2 - 1) * wobbleMM;
                    outPts[i] = dense[i] + normal * w;
                }
                return new Curve { Points = outPts, Seed = r.Next() };
            }
            return new Curve { Points = dense.ToArray(), Seed = r.Next() };
        }

        static List<Vector2> Bezier(Vector2 a, Vector2 b, Vector2 c, int steps = 16)
        {
            var list = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, u = 1 - t;
                list.Add(a * (u * u) + b * (2 * u * t) + c * (t * t));
            }
            return list;
        }

        static void Add(Context ctx, Curve c, int zone)
        {
            c.Zone = zone;
            c.Finish();
            ctx.Curves.Add(c);
        }

        // Builds the right side of a paired zone with rightRng and its mirror with leftRng-built twins.
        static void BuildCurves(Context ctx, Random rng)
        {
            var l = ctx.L;
            int seedRight = rng.Next(), seedLeft = rng.Next(), seedMid = rng.Next();

            // Forehead: full-width lines arching over each brow and dipping at the centre. Each line is one curve
            // across the midline, so with both brows raised it reads as one line. Its height is masked by whichever
            // forehead zone covers the texel (the two zones touch at the midline), so each brow still drives its own
            // half. Each side takes its extent, height, depth and width from its own random stream; depth wander and
            // breaks differ along the line.
            {
                var r = new Random(seedMid);
                var rCentre = new Random(seedMid ^ 0x7A3); // separate stream: the rest of the forehead keeps its layout
                float baseY = BrowHeight(ctx, ctx.Brow.X) - 4f; // the arch lifts the lowest line to about 5 mm above the brow
                float top = l.ForeheadTop - 6f;
                int k = 0;
                for (float y = baseY; y < top; k++)
                {
                    float arch = Math.Max(1.5f, 6f - k * 1.3f);
                    float depthScale = k == 0 ? 0.8f : 1f;
                    float yCentre = Rand(rCentre, -1.2f, 1.2f);
                    var pts = new List<Vector2>();
                    float depth = 0, width = 0, breaks = 0;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var rs = new Random(r.Next());
                        Rand(rs, 0f, 1f); // was the inner end of a half line; keeps the stream for the fine line below
                        float outer = Rand(rs, 48f, 64f);
                        float yOff = Rand(rs, -1.2f, 1.2f);
                        var halfLine = new List<Vector2>();
                        for (float s = side < 0 ? 0f : 2f; s <= outer; s += 2f)
                        {
                            float g = (float)Math.Exp(-Math.Pow((s - ctx.Brow.X) / 19f, 2));
                            float own = Smooth(3f, 16f, s); // 0 at the midline, where both sides meet at yCentre
                            halfLine.Add(new Vector2(s * side, y + yCentre + (yOff - yCentre) * own + arch * g - 0.0006f * s * s * (k * 0.5f)));
                        }
                        if (side < 0) halfLine.Reverse();
                        pts.AddRange(halfLine);
                        Curve c = null;
                        if (side > 0) c = Line(rs, pts, 0.6f, 12f);
                        else { rs.Next(); rs.Next(); } // the random numbers Line takes
                        depth += Rand(rs, 0.75f, 0.95f) * depthScale * 0.5f; width += Rand(rs, 1.2f, 1.5f) * 0.5f; breaks += Rand(rs, 0f, 0.18f) * 0.5f;
                        if (c != null)
                        {
                            c.Depth = depth; c.Width = width; c.Shoulder = 3.6f; c.Roll = 0.4f;
                            c.TaperStart = c.TaperEnd = 10f; c.Wander = 0.4f; c.Breaks = breaks;
                            c.PairZone = (int)UMAWrinkleZone.ForeheadLeft;
                            Add(ctx, c, (int)UMAWrinkleZone.ForeheadRight);
                        }
                        // a finer line between this one and the next
                        var fine = new List<Vector2>();
                        float fi = Rand(rs, 4f, 16f), fo = Rand(rs, 36f, 56f), half = Rand(rs, 3.8f, 5.2f);
                        for (float s = fi; s <= fo; s += 2f)
                        {
                            float g = (float)Math.Exp(-Math.Pow((s - ctx.Brow.X) / 21f, 2));
                            fine.Add(new Vector2(s * side, y + yOff + half + (arch - 0.4f) * g));
                        }
                        if (side < 0) fine.Reverse();
                        if (y + half < top)
                        {
                            var cf = Line(rs, fine, 0.5f, 6f);
                            cf.Depth = Rand(rs, 0.12f, 0.18f); cf.Width = 0.45f; cf.Shoulder = 2.6f; cf.TaperStart = cf.TaperEnd = 5f;
                            cf.Wander = 0.5f; cf.Breaks = Rand(rs, 0.12f, 0.28f);
                            Add(ctx, cf, side > 0 ? (int)UMAWrinkleZone.ForeheadRight : (int)UMAWrinkleZone.ForeheadLeft);
                        }
                    }
                    y += Rand(r, 7.4f, 9.2f);
                }
            }

            // Glabella: two deep vertical lines leaning outward at the top, shorter ones beside them.
            {
                var r = new Random(seedMid ^ 0x51);
                float bottom = ctx.NoseRoot.Y + 11f, top = ctx.NoseRoot.Y + 33f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x0 = Rand(r, 5.5f, 7f), x1 = x0 + Rand(r, 1.5f, 3f);
                    var c = Line(r, Bezier(new Vector2(x0 * side, bottom + Rand(r, 0, 2)), new Vector2((x0 + 0.6f) * side, (bottom + top) * 0.5f), new Vector2(x1 * side, top - Rand(r, 0, 3))), 0.25f, 5f);
                    c.Depth = Rand(r, 1.4f, 1.7f); c.Width = Rand(r, 1.3f, 1.5f); c.Shoulder = 3.5f; c.Roll = 0.35f; c.TaperStart = 4f; c.TaperEnd = 5f; c.Wander = 0.3f;
                    Add(ctx, c, (int)UMAWrinkleZone.Glabella);
                    float x2 = x0 + Rand(r, 5.5f, 7.5f);
                    var c2 = Line(r, Bezier(new Vector2(x2 * side, bottom + 4), new Vector2((x2 + 0.8f) * side, bottom + 8), new Vector2((x2 + 2.5f) * side, top - 4)), 0.3f, 4f);
                    c2.Depth = Rand(r, 0.5f, 0.7f); c2.Width = 0.8f; c2.Shoulder = 2.8f; c2.TaperStart = c2.TaperEnd = 3.5f; c2.Breaks = 0.2f;
                    Add(ctx, c2, (int)UMAWrinkleZone.Glabella);
                    // corrugator folds: curving up and outward above the inner end of the brow
                    for (int k = 0; k < 2; k++)
                    {
                        float lift = k * Rand(r, 3f, 4f);
                        var a0 = new Vector2((x0 + 5.5f + k * 1.5f) * side, top - 10f + lift);
                        var a1 = new Vector2((x0 + 10f + k * 1.5f) * side, top - 4.5f + lift);
                        var a2 = new Vector2((x0 + 15.5f) * side, top - 5f + lift);
                        var ck = Line(r, Bezier(a0, a1, a2, 12), 0.3f, 4f);
                        ck.Depth = Rand(r, 0.55f, 0.75f) * (k == 0 ? 1f : 0.7f); ck.Width = Rand(r, 1f, 1.2f); ck.Shoulder = 3f; ck.Roll = 0.35f;
                        ck.TaperStart = 3f; ck.TaperEnd = 4f; ck.Wander = 0.35f; ck.Breaks = 0.1f;
                        Add(ctx, ck, (int)UMAWrinkleZone.Glabella);
                    }
                }
            }

            // Nose root: two or three short horizontal lines across the bridge.
            {
                var r = new Random(seedMid ^ 0x77);
                for (int k = 0; k < 3; k++)
                {
                    float y = ctx.NoseRoot.Y + 1f + k * Rand(r, 2.6f, 3.2f);
                    float half = Rand(r, 7f, 10f) - k * 1.2f;
                    var c = Line(r, Bezier(new Vector2(-half, y + 0.8f), new Vector2(0, y - 0.6f), new Vector2(half, y + 0.8f)), 0.3f, 4f);
                    c.Depth = Rand(r, 0.7f, 0.9f) * (k == 1 ? 1f : 0.75f); c.Width = Rand(r, 0.75f, 0.9f); c.Shoulder = 3.5f; c.Roll = 0.35f; c.TaperStart = c.TaperEnd = 3.5f; c.Wander = 0.4f; c.Breaks = 0.15f;
                    Add(ctx, c, (int)UMAWrinkleZone.NoseRoot);
                }
            }

            // Paired zones: build the right side, mirror it with the left side's own random stream.
            var right = new Random(seedRight);
            var left = new Random(seedLeft);
            BuildSide(ctx, right, 1);
            BuildSide(ctx, left, -1);

            // Lip lines: fine lines across the borders of both lips, more above than below.
            {
                var r = new Random(seedMid ^ 0x1234);
                for (float s = -16f; s <= 16f; s += Rand(r, 1.7f, 2.8f))
                {
                    float rise = Rand(r, 3.5f, 9f) * (1 - Math.Abs(s) / 26f);
                    float lean = s * 0.12f;
                    float y0 = l.UpperLipBorder - 0.5f - 0.012f * s * s;
                    var c = Line(r, new[] { new Vector2(s, y0), new Vector2(s + lean, Math.Min(y0 + rise, l.NoseBase - 2)) }, 0.15f, 3f);
                    c.Depth = Rand(r, 0.18f, 0.3f); c.Width = Rand(r, 0.32f, 0.4f); c.Shoulder = 2.4f; c.TaperStart = 0.8f; c.TaperEnd = 2.5f; c.Wander = 0.5f; c.Breaks = 0.15f;
                    Add(ctx, c, (int)UMAWrinkleZone.LipLines);
                }
                for (float s = -12f; s <= 12f; s += Rand(r, 2.4f, 3.6f))
                {
                    float drop = Rand(r, 2f, 5f) * (1 - Math.Abs(s) / 20f);
                    float y0 = l.LowerLipBorder + 0.5f + 0.01f * s * s;
                    var c = Line(r, new[] { new Vector2(s, y0), new Vector2(s * 1.1f, y0 - drop) }, 0.15f, 3f);
                    c.Depth = Rand(r, 0.08f, 0.15f); c.Width = 0.28f; c.Shoulder = 2.4f; c.TaperStart = 0.8f; c.TaperEnd = 2f; c.Wander = 0.5f; c.Breaks = 0.2f;
                    Add(ctx, c, (int)UMAWrinkleZone.LipLines);
                }
                // vertical folds of the lips themselves
                for (float s = -18f; s <= 18f; s += Rand(r, 1.3f, 2.1f))
                {
                    float edge = Math.Abs(s) / 21f;
                    for (int lip = 0; lip < 2; lip++)
                    {
                        float yA = lip == 0 ? l.MouthLine + 1.2f : l.MouthLine - 1.2f;
                        float yB = lip == 0 ? l.UpperLipBorder - 0.8f - 0.01f * s * s : l.LowerLipBorder + 1f + 0.008f * s * s;
                        if ((lip == 0 && yB - yA < 1.5f) || (lip == 1 && yA - yB < 1.5f)) continue;
                        var c = Line(r, new[] { new Vector2(s, yA), new Vector2(s * 1.04f, yB) }, 0.12f, 2f);
                        c.Depth = Rand(r, 0.05f, 0.11f) * (1 - edge * 0.6f); c.Width = 0.22f; c.Shoulder = 2.2f; c.TaperStart = c.TaperEnd = 1f; c.Wander = 0.6f; c.Breaks = 0.25f; c.Flush = 0.3f;
                        Add(ctx, c, (int)UMAWrinkleZone.LipLines);
                    }
                }
            }

            // Chin: dimples from the chin muscle and a deeper crease under the lower lip.
            {
                var r = new Random(seedMid ^ 0x4321);
                var dimples = new List<Vector2>();
                for (int tries = 0; tries < 400 && dimples.Count < 34; tries++)
                {
                    var p = ctx.Chin + new Vector2(Rand(r, -14f, 14f), Rand(r, -9f, 9f));
                    if (((p - ctx.Chin) / new Vector2(14f, 9f)).Length() > 1) continue;
                    bool close = false;
                    foreach (var q in dimples) if (Vector2.Distance(p, q) < 2.6f) { close = true; break; }
                    if (close) continue;
                    dimples.Add(p);
                    var c = new Curve { Points = new[] { p }, Seed = r.Next() };
                    c.Depth = Rand(r, 0.1f, 0.24f); c.Width = Rand(r, 0.8f, 1.3f); c.Shoulder = 1.9f; c.TaperStart = c.TaperEnd = 0; c.Wander = 0; c.Flush = 0.5f;
                    Add(ctx, c, (int)UMAWrinkleZone.Chin);
                }
                float y = l.LowerLipBorder - 6.5f;
                var crease = Line(r, Bezier(new Vector2(-13, y + 2.5f), new Vector2(0, y - 1.5f), new Vector2(13, y + 2.5f)), 0.35f, 5f);
                crease.Depth = 0.35f; crease.Width = 0.9f; crease.Shoulder = 2.6f; crease.TaperStart = crease.TaperEnd = 5f; crease.Wander = 0.3f;
                Add(ctx, crease, (int)UMAWrinkleZone.Chin);
            }
        }

        static void BuildSide(Context ctx, Random r, int side)
        {
            var l = ctx.L;
            int crow = side > 0 ? (int)UMAWrinkleZone.CrowsFeetRight : (int)UMAWrinkleZone.CrowsFeetLeft;
            int naso = side > 0 ? (int)UMAWrinkleZone.NasolabialRight : (int)UMAWrinkleZone.NasolabialLeft;
            int corner = side > 0 ? (int)UMAWrinkleZone.MouthCornerRight : (int)UMAWrinkleZone.MouthCornerLeft;
            Vector2 S(float x, float y) { return new Vector2(x * side, y); }

            // Crow's feet: a fan from beyond the outer corner of the eye, lines bending down as they leave the eye.
            var center = new Vector2(ctx.EyeOuter.X + 4.5f, ctx.EyeOuter.Y);
            float[] angles = { -40, -26, -12, 2, 16, 30, 42 };
            foreach (float a0 in angles)
            {
                float a = a0 + Rand(r, -5, 5);
                float r0 = Rand(r, 2.5f, 6f), r1 = Rand(r, 18f, 26f) - Math.Abs(a) * 0.08f;
                float rad = a * (float)Math.PI / 180f;
                var dir = new Vector2((float)Math.Cos(rad), (float)Math.Sin(rad));
                var bend = new Vector2(0, -Rand(r, 0.5f, 2.5f) - Math.Max(0, -a) * 0.03f);
                var p0 = center + dir * r0; var p2 = center + dir * r1 + bend; var p1 = center + dir * ((r0 + r1) * 0.5f) + bend * 0.2f;
                var c = Line(r, Bezier(S(p0.X, p0.Y), S(p1.X, p1.Y), S(p2.X, p2.Y), 12), 0.3f, 5f);
                float central = 1 - Math.Abs(a) / 60f;
                c.Depth = Rand(r, 0.6f, 0.85f) * (0.6f + 0.4f * central); c.Width = Rand(r, 0.65f, 0.8f); c.Shoulder = 3.4f; c.Roll = 0.35f;
                c.TaperStart = 3f; c.TaperEnd = 7f; c.Wander = 0.45f; c.Breaks = Rand(r, 0f, 0.2f);
                Add(ctx, c, crow);
                // a finer branch splitting off part way out
                if (r.NextDouble() < 0.6)
                {
                    float split = Rand(r, 0.35f, 0.6f);
                    var ps = Vector2.Lerp(p0, p2, split);
                    float ba = a + Rand(r, 8, 16) * (r.NextDouble() < 0.5 ? -1 : 1);
                    float brad = ba * (float)Math.PI / 180f;
                    var pe = ps + new Vector2((float)Math.Cos(brad), (float)Math.Sin(brad)) * Rand(r, 5f, 9f);
                    var b = Line(r, new[] { S(ps.X, ps.Y), S(pe.X, pe.Y) }, 0.25f, 3f);
                    b.Depth = c.Depth * 0.45f; b.Width = 0.38f; b.Shoulder = 2.6f; b.TaperStart = 1.5f; b.TaperEnd = 3f; b.Breaks = 0.2f;
                    Add(ctx, b, crow);
                }
            }
            // fine lines between the main ones, mostly broken
            for (int k = 0; k < 9; k++)
            {
                float a = Rand(r, -46, 46), rad = a * (float)Math.PI / 180f;
                var dir = new Vector2((float)Math.Cos(rad), (float)Math.Sin(rad));
                float r0 = Rand(r, 5f, 10f), r1 = r0 + Rand(r, 5f, 10f);
                var p0 = center + dir * r0; var p1 = center + dir * r1 + new Vector2(0, -0.8f);
                var c = Line(r, new[] { S(p0.X, p0.Y), S(p1.X, p1.Y) }, 0.25f, 3f);
                c.Depth = Rand(r, 0.08f, 0.15f); c.Width = 0.32f; c.Shoulder = 2.4f; c.TaperStart = c.TaperEnd = 2f; c.Wander = 0.6f; c.Breaks = 0.4f;
                Add(ctx, c, crow);
            }
            // folds under the lower lid, roughly following the lid
            for (int k = 0; k < 3; k++)
            {
                float rx = l.EyeRadius.X + 0.5f + k * Rand(r, 2.2f, 3f), ry = l.EyeRadius.Y + 1.5f + k * Rand(r, 2f, 2.8f);
                float from = Rand(r, -150, -128), to = Rand(r, -48, -28);
                var pts = new List<Vector2>();
                for (float a = from; a <= to; a += 6)
                {
                    float rad = a * (float)Math.PI / 180f;
                    var p = ctx.Eye + new Vector2(rx * (float)Math.Cos(rad), ry * (float)Math.Sin(rad)) + new Vector2(1.5f, 0);
                    pts.Add(S(p.X, p.Y));
                }
                if (side < 0) pts.Reverse();
                var c = Line(r, pts, 0.3f, 4f);
                c.Depth = Rand(r, 0.35f, 0.45f) * (k == 0 ? 1.2f : 1f); c.Width = Rand(r, 0.55f, 0.65f); c.Shoulder = 3f; c.Roll = 0.35f;
                c.TaperStart = c.TaperEnd = 5f; c.Wander = 0.5f; c.Breaks = Rand(r, 0.15f, 0.35f);
                Add(ctx, c, crow);
            }

            // Bunny lines: oblique creases down the side of the nose bridge in a sneer.
            {
                float top = ctx.NoseRoot.Y - 4f;
                var perp = Vector2.Normalize(new Vector2(0.83f, 0.55f));
                for (int k = 0; k < 4; k++)
                {
                    var p0 = new Vector2(4.5f, top) + perp * (k * Rand(r, 2f, 2.5f)) + new Vector2(0, -k * 1.2f);
                    var p2 = p0 + new Vector2(Rand(r, 4.5f, 6f), -Rand(r, 8f, 11f)) * (k == 3 ? 0.6f : 1f);
                    var p1 = Vector2.Lerp(p0, p2, 0.5f) + new Vector2(0.7f, 0.5f);
                    var c = Line(r, Bezier(S(p0.X, p0.Y), S(p1.X, p1.Y), S(p2.X, p2.Y), 10), 0.2f, 4f);
                    c.Depth = Rand(r, 0.6f, 0.8f) * (k == 3 ? 0.6f : 1f); c.Width = Rand(r, 0.6f, 0.72f); c.Shoulder = 3f; c.Roll = 0.35f;
                    c.TaperStart = 2.5f; c.TaperEnd = 4f; c.Wander = 0.35f; c.Breaks = 0.1f;
                    Add(ctx, c, (int)UMAWrinkleZone.BunnyLines);
                }
            }

            // Nasolabial fold: a crease from the nostril wing to beside the mouth corner, the cheek rolling over it.
            {
                var a = ctx.NoseWing + new Vector2(2.5f, -3f); // in the groove beside the nostril wing, not on it
                var c0 = new Vector2(ctx.Mouth.X + 5.5f, ctx.Mouth.Y - 7f);
                var mid = new Vector2(a.X + 10f, (a.Y + c0.Y) * 0.5f + 4f);
                var pts = Bezier(S(a.X, a.Y), S(mid.X, mid.Y), S(c0.X, c0.Y), 24);
                var c = Line(r, pts, 0.35f, 8f);
                c.Depth = Rand(r, 0.45f, 0.55f); c.Width = Rand(r, 1.1f, 1.3f); c.Shoulder = 3f;
                // the roll sits on the cheek (lateral) side; the curve runs downward, so lateral is to its left on the right side
                c.Bulge = Rand(r, 1.6f, 2f); c.BulgeOffset = 5f; c.BulgeWidth = 5.5f; c.BulgeSide = side > 0 ? 1 : -1;
                c.TaperStart = 9f; c.TaperEnd = 9f; c.Wander = 0.25f;
                FixBulgeSide(c, side);
                Add(ctx, c, naso);
                // a faint second line further out on the cheek
                var outer = new List<Vector2>();
                foreach (var p in Bezier(a + new Vector2(6.5f, -6f), mid + new Vector2(7f, -2f), c0 + new Vector2(6f, 2f), 16)) outer.Add(S(p.X, p.Y));
                var c2 = Line(r, outer, 0.5f, 6f);
                c2.Depth = Rand(r, 0.1f, 0.16f); c2.Width = 0.55f; c2.Shoulder = 2.6f; c2.TaperStart = c2.TaperEnd = 6f; c2.Breaks = 0.35f; c2.Wander = 0.5f;
                Add(ctx, c2, naso);
            }

            // Mouth corner: a short crease continuing down from the corner, with a small roll beside it.
            {
                var a = new Vector2(ctx.Mouth.X + 1.5f, ctx.Mouth.Y - 1.5f);
                var b = new Vector2(ctx.Mouth.X + 4.5f, ctx.Mouth.Y - 11f);
                var e = new Vector2(ctx.Mouth.X + 5.5f, ctx.Mouth.Y - 23f);
                var c = Line(r, Bezier(S(a.X, a.Y), S(b.X, b.Y), S(e.X, e.Y), 16), 0.3f, 6f);
                c.Depth = Rand(r, 0.3f, 0.4f); c.Width = Rand(r, 0.75f, 0.85f); c.Shoulder = 2.8f;
                c.Bulge = Rand(r, 0.5f, 0.6f); c.BulgeOffset = 3.2f; c.BulgeWidth = 3.2f;
                c.TaperStart = 3f; c.TaperEnd = 8f; c.Wander = 0.35f;
                FixBulgeSide(c, side);
                Add(ctx, c, corner);
                // the small crease right at the corner of the lips
                var k = Line(r, new[] { S(ctx.Mouth.X - 1.5f, ctx.Mouth.Y + 0.3f), S(ctx.Mouth.X + 3.5f, ctx.Mouth.Y + 1.2f) }, 0.15f, 2f);
                k.Depth = 0.18f; k.Width = 0.38f; k.Shoulder = 2.4f; k.TaperStart = 1.2f; k.TaperEnd = 2.5f;
                Add(ctx, k, corner);
            }
        }

        // The roll belongs on the side away from the midline. Works out which side of the curve that is.
        static void FixBulgeSide(Curve c, int side)
        {
            var p = c.Points;
            var dir = p[p.Length - 1] - p[0];
            // left of the direction (dir rotated +90 degrees) points away from the midline when its x has the side's sign
            var leftNormal = new Vector2(-dir.Y, dir.X);
            c.BulgeSide = Math.Sign(leftNormal.X) == side ? 1 : -1;
        }

        // ---- Evaluation ---------------------------------------------------------------------------------------

        static void BuildGrid(Context ctx)
        {
            ctx.GridN = (int)Math.Ceiling(280 / ctx.GridCell);
            var cells = new List<long>[ctx.GridN * ctx.GridN];
            for (int i = 0; i < ctx.Curves.Count; i++)
            {
                var c = ctx.Curves[i];
                int segments = Math.Max(1, c.Points.Length - 1);
                for (int k = 0; k < segments; k++)
                {
                    var a = c.Points[k]; var b = c.Points[Math.Min(k + 1, c.Points.Length - 1)];
                    var lo = Vector2.Min(a, b) - new Vector2(c.Reach); var hi = Vector2.Max(a, b) + new Vector2(c.Reach);
                    int x0 = CellX(ctx, lo.X), x1 = CellX(ctx, hi.X), y0 = CellY(ctx, lo.Y), y1 = CellY(ctx, hi.Y);
                    long key = ((long)i << 16) | (long)(k + 1);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int g = y * ctx.GridN + x;
                            (cells[g] ?? (cells[g] = new List<long>())).Add(key);
                        }
                }
            }
            ctx.Grid = new long[cells.Length][];
            for (int g = 0; g < cells.Length; g++) if (cells[g] != null) { cells[g].Sort(); ctx.Grid[g] = cells[g].ToArray(); }
        }

        const float GridHeightOrigin = 1700f;
        static int CellX(Context ctx, float s) { int c = (int)Math.Floor((s - ctx.GridMin) / ctx.GridCell); return c < 0 ? 0 : c >= ctx.GridN ? ctx.GridN - 1 : c; }
        static int CellY(Context ctx, float z) { int c = (int)Math.Floor((z - GridHeightOrigin + 20f) / ctx.GridCell); return c < 0 ? 0 : c >= ctx.GridN ? ctx.GridN - 1 : c; }

        static void Nearest(Curve c, Vector2 f, out float signedDistance, out float along)
        {
            var p = c.Points;
            if (p.Length == 1) { signedDistance = Vector2.Distance(f, p[0]); along = 0; return; }
            float best = float.MaxValue; signedDistance = 0; along = 0;
            for (int i = 0; i + 1 < p.Length; i++)
            {
                var a = p[i]; var ab = p[i + 1] - a;
                float len2 = ab.LengthSquared();
                float t = len2 > 0 ? Clamp01(Vector2.Dot(f - a, ab) / len2) : 0;
                var q = a + ab * t;
                float d2 = Vector2.DistanceSquared(f, q);
                if (d2 < best)
                {
                    best = d2;
                    float cross = ab.X * (f.Y - a.Y) - ab.Y * (f.X - a.X);
                    signedDistance = (float)Math.Sqrt(d2) * (cross >= 0 ? 1 : -1);
                    along = c.Along[i] + (float)Math.Sqrt(len2) * t;
                }
            }
        }

        // Distance from f to segment k of curve c: returns the squared distance, and the signed distance and arc length.
        static float Segment(Curve c, int k, Vector2 f, out float signedDistance, out float along)
        {
            var p = c.Points;
            if (p.Length == 1) { float d1 = Vector2.Distance(f, p[0]); signedDistance = d1; along = 0; return d1 * d1; }
            var a = p[k]; var ab = p[k + 1] - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 0 ? Clamp01(Vector2.Dot(f - a, ab) / len2) : 0;
            float d2 = Vector2.DistanceSquared(f, a + ab * t);
            float cross = ab.X * (f.Y - a.Y) - ab.Y * (f.X - a.X);
            signedDistance = (float)Math.Sqrt(d2) * (cross >= 0 ? 1 : -1);
            along = c.Along[k] + (float)Math.Sqrt(len2) * t;
            return d2;
        }

        // Width of the blend between candidate nearest points, mm. Far out on a wavy line the nearest point can jump
        // between two distant parts of the line; blending their positions along the line keeps the result smooth.
        const float SoftNearestMM = 0.8f;

        static Contribution Evaluate(Context ctx, Vector2 f, float[] zoneMask)
        {
            var result = new Contribution();
            var list = ctx.Grid[CellY(ctx, f.Y) * ctx.GridN + CellX(ctx, f.X)];
            if (list == null) return result;
            int start = 0;
            while (start < list.Length)
            {
                int curve = (int)(list[start] >> 16);
                int end = start + 1;
                while (end < list.Length && (int)(list[end] >> 16) == curve) end++;
                var c = ctx.Curves[curve];
                if (f.X >= c.Min.X && f.X <= c.Max.X && f.Y >= c.Min.Y && f.Y <= c.Max.Y)
                {
                    float best = float.MaxValue, bestD = 0;
                    for (int e = start; e < end; e++)
                    {
                        float d2 = Segment(c, (int)(list[e] & 0xFFFF) - 1, f, out float d, out float along);
                        if (d2 < best) { best = d2; bestD = d; }
                    }
                    float nearest = Math.Abs(bestD);
                    if (nearest <= c.Reach)
                    {
                        // soft nearest position along the line
                        float sum = 0, weighted = 0;
                        for (int e = start; e < end; e++)
                        {
                            Segment(c, (int)(list[e] & 0xFFFF) - 1, f, out float d, out float along);
                            float x = (Math.Abs(d) - nearest) / SoftNearestMM;
                            if (x > 3) continue;
                            float w = (float)Math.Exp(-x * x);
                            sum += w; weighted += w * along;
                        }
                        float mask = zoneMask[c.Zone] + (c.PairZone > 0 ? zoneMask[c.PairZone] : 0f);
                        Accumulate(c, bestD, sum > 0 ? weighted / sum : 0, mask, ref result);
                    }
                }
                start = end;
            }
            return result;
        }

        static void Accumulate(Curve c, float d, float along, float zoneMask, ref Contribution result)
        {
            if (Math.Abs(d) > c.Reach) return;
            float envelope = 1;
            if (c.Points.Length > 1)
            {
                envelope = Smooth(0, Math.Max(c.TaperStart, 0.01f), along) * Smooth(c.Length, c.Length - Math.Max(c.TaperEnd, 0.01f), along);
                // depth wanders along the line, and some stretches drop out
                float wander = Noise1(along / 7f, c.Seed);
                envelope *= 1 - c.Wander + c.Wander * (0.35f + 1.3f * wander);
                if (c.Breaks > 0) envelope *= Smooth(c.Breaks - 0.25f, c.Breaks + 0.25f, Noise1(along / 9f + 17.3f, c.Seed ^ 0x2F)); // fades over millimetres, never a cut
            }
            // Each line only exists inside its own zone, so one zone never shows another zone's lines.
            envelope *= Smooth(0, 0.5f, zoneMask);
            if (envelope <= 0) return;
            float width = c.Width * (c.Points.Length > 1 ? 0.8f + 0.45f * Noise1(along / 9f + 5.1f, c.Seed ^ 0x6B) : 1f);
            float u = d / width, us = d / (width * c.Shoulder);
            float valley = (float)Math.Exp(-u * u);
            float shoulder = (float)Math.Exp(-us * us) * (c.Roll >= 0 ? c.Roll : 1f / c.Shoulder);
            float h = c.Depth * envelope * (shoulder - valley);
            if (c.Bulge > 0)
            {
                float off = (d * c.BulgeSide - c.BulgeOffset) / c.BulgeWidth;
                h += c.Bulge * envelope * (float)Math.Exp(-off * off);
            }
            result.Height += h;
            result.Crease += c.Depth * envelope * valley;
            float wide = d / (width * 3.5f);
            result.Flush += c.Flush * envelope * Math.Min(1f, c.Depth / 0.35f) * (float)Math.Exp(-wide * wide);
        }

        // Smooth 1D value noise in 0..1.
        static float Noise1(float x, int seed)
        {
            int i = (int)Math.Floor(x); float t = x - i;
            t = t * t * t * (t * (t * 6 - 15) + 10);
            return Hash(i, seed) * (1 - t) + Hash(i + 1, seed) * t;
        }

        static float Hash(int x, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + seed * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
