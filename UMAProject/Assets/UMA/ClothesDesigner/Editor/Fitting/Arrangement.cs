using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Lays the flat pieces around the body before sewing, each round its body part and wrapped without
    /// stretching: torso pieces on an upright elliptical cylinder a little larger than the torso, with
    /// their top just above the shoulders; head pieces on one round the head; arm pieces on a cylinder
    /// along the arm from the shoulder joint; leg pieces on an upright cylinder round the leg, turning
    /// into the torso's cylinder between the crotch and the hips, so a trouser piece starts round its
    /// leg below and round its quarter of the hips above. A flat piece is laid beside its part instead.
    /// </summary>
    public static class Arrangement
    {
        public sealed class Result
        {
            public Vector3[] positions;
            /// <summary>True when the mesh's triangles face into the body and must be turned for drawing and baking.</summary>
            public bool flipTriangles;
            public float gap;
            public Vector2 semiAxes;
            public readonly List<string> messages = new List<string>();
        }

        /// <summary>A leg piece goes from the torso's cylinder to the leg's over at least this height above the crotch.</summary>
        public const float LegBlendCm = 12f;

        private static readonly Vector3 Front = Vector3.forward;
        /// <summary>The body's left: the renderer's -x, as the torso's 90 degrees.</summary>
        private static readonly Vector3 BodyLeft = Vector3.left;

        /// <param name="fixedGap">
        /// For a live preview while a piece is dragged: use this gap and skip the check that no point
        /// starts in the body (which may push every piece farther out). Null for the full arrangement.
        /// </param>
        public static Result Arrange(ClothesPattern pattern, ClothesFit fit, PatternMesh mesh, BodyReference body, float? fixedGap = null)
        {
            var result = new Result();
            BodyLandmarks marks = body.Landmarks;
            int pieces = pattern.pieces.Count;
            var top = new float[pieces];
            var centerX = new float[pieces];
            var minX = new float[pieces];
            var maxX = new float[pieces];
            for (int p = 0; p < pieces; p++) { top[p] = float.MinValue; minX[p] = float.MaxValue; maxX[p] = float.MinValue; }
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int p = mesh.piece[v];
                top[p] = Mathf.Max(top[p], mesh.layout[v].y);
                minX[p] = Mathf.Min(minX[p], mesh.layout[v].x);
                maxX[p] = Mathf.Max(maxX[p], mesh.layout[v].x);
            }
            for (int p = 0; p < pieces; p++) centerX[p] = (minX[p] + maxX[p]) * 0.5f;

            var piecePlacement = new PiecePlacement[pieces];
            var part = new BodyPart[pieces];
            bool fellBack = false;
            for (int p = 0; p < pieces; p++)
            {
                piecePlacement[p] = fit.PlacementFor(pattern.pieces[p]);
                part[p] = piecePlacement[p].part;
                if (part[p] != BodyPart.Torso && !marks.limbs) { part[p] = BodyPart.Torso; fellBack = true; }
            }
            if (fellBack) result.messages.Add("This body has no humanoid arms, legs and head to lay pieces round; those pieces are laid round the torso.");

            // The torso's cylinder spans the torso pieces (and the hips, for trousers).
            float lowest = float.MaxValue;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int p = mesh.piece[v];
                if (part[p] != BodyPart.Torso) continue;
                float worldTop = marks.shoulderTopY + piecePlacement[p].topAboveShoulderCm * 0.01f;
                lowest = Mathf.Min(lowest, worldTop - (top[p] - mesh.layout[v].y) * 0.01f);
            }
            if (lowest == float.MaxValue) lowest = marks.limbs ? marks.crotchY + 0.02f : marks.hipY;
            lowest = Mathf.Min(lowest, marks.shoulderTopY - 0.1f);

            Vector2 half = body.HalfExtents(lowest, marks.shoulderTopY - 0.05f);
            Vector2 center = marks.torsoCenter;
            float clearance = fit.clearanceCm * 0.01f;
            float gap = fixedGap ?? 0.04f;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float a = half.x + gap, b = half.y + gap;
                // Arc tables by ellipse: a piece's Distance moves it out (or in) from the others.
                var tables = new Dictionary<Vector2, ArcTable>();
                ArcTable Table(Vector2 axes)
                {
                    if (!tables.TryGetValue(axes, out ArcTable table)) tables[axes] = table = new ArcTable(axes.x, axes.y);
                    return table;
                }
                var positions = new Vector3[mesh.VertexCount];
                var outward = new Vector3[mesh.VertexCount];
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    int p = mesh.piece[v];
                    PiecePlacement placement = piecePlacement[p];
                    float extra = Mathf.Max(-gap + 0.005f, placement.distanceCm * 0.01f);
                    float x = (mesh.layout[v].x - centerX[p] + placement.acrossCm) * 0.01f;
                    float down = (top[p] - mesh.layout[v].y) * 0.01f;
                    // Folded pleat layers lie a little under the one above them, towards the body.
                    float under = mesh.layoutDepth[v] * 0.01f;
                    Vector3 position, normal;
                    switch (part[p])
                    {
                        case BodyPart.Head:
                        {
                            Vector2 axes = new Vector2(marks.headHalf.x + gap + extra, marks.headHalf.y + gap + extra);
                            float y = marks.headTopY + placement.topAboveShoulderCm * 0.01f - down;
                            position = OnEllipse(Table(axes), marks.headCenter, axes, placement, x, y, under, out normal);
                            break;
                        }
                        case BodyPart.LeftArm:
                        case BodyPart.RightArm:
                        {
                            bool left = part[p] == BodyPart.LeftArm;
                            marks.ArmFrame(left, out Vector3 root, out Vector3 axis, out Vector3 up, out Vector3 side);
                            // 90 is the front of either arm; the left arm's frame turns the other way.
                            float angle = (left ? -placement.angle : placement.angle) * Mathf.Deg2Rad;
                            Vector3 c = root + axis * (down - placement.topAboveShoulderCm * 0.01f);
                            float r = Mathf.Max(0.01f, marks.armRadius + gap + extra - under);
                            position = OnCylinder(c, axis, up, side, r, angle, x, placement.flat, out normal);
                            break;
                        }
                        case BodyPart.LeftLeg:
                        case BodyPart.RightLeg:
                        {
                            bool left = part[p] == BodyPart.LeftLeg;
                            float y = marks.waistY + placement.topAboveShoulderCm * 0.01f - down;
                            Vector3 c = marks.LegAxisAt(left, y);
                            c.y = y;
                            float r = Mathf.Max(0.01f, marks.legRadius + gap + extra - under);
                            float angle = placement.angle * Mathf.Deg2Rad;
                            position = OnCylinder(c, Vector3.down, Front, BodyLeft, r, angle, x, placement.flat, out normal);
                            // Between the crotch and the hips the leg's cylinder turns into the torso's.
                            float blendTop = Mathf.Max(marks.hipY, marks.crotchY + LegBlendCm * 0.01f);
                            float span = blendTop - marks.crotchY;
                            float w = placement.flat ? 1f : Mathf.SmoothStep(0f, 1f, (blendTop - y) / span);
                            if (w < 1f)
                            {
                                Vector2 axes = new Vector2(a + extra, b + extra);
                                var onTorso = new PiecePlacement { angle = TorsoAngleForLeg(left, placement.angle) };
                                Vector3 torso = OnEllipse(Table(axes), center, axes, onTorso, x, y, under, out Vector3 torsoNormal);
                                position = Vector3.Lerp(torso, position, w);
                                normal = Vector3.Lerp(torsoNormal, normal, w);
                            }
                            break;
                        }
                        default:
                        {
                            Vector2 axes = new Vector2(a + extra, b + extra);
                            float y = marks.shoulderTopY + placement.topAboveShoulderCm * 0.01f - down;
                            position = OnEllipse(Table(axes), center, axes, placement, x, y, under, out normal);
                            break;
                        }
                    }
                    positions[v] = position;
                    outward[v] = normal;
                }
                int inside = 0;
                if (!fixedGap.HasValue)
                {
                    for (int v = 0; v < positions.Length; v++)
                    {
                        if (body.Collider.Closest(positions[v], out _, out _, out float d) && d < clearance + 0.005f) inside++;
                    }
                }
                if (inside == 0 || attempt == 7)
                {
                    result.positions = positions;
                    result.gap = gap;
                    result.semiAxes = new Vector2(a, b);
                    if (inside > 0) result.messages.Add(inside + " points of the cloth start inside or against the body. Move the pieces further out before simulating.");
                    else if (attempt > 0) result.messages.Add("The pieces were moved " + Mathf.RoundToInt((gap - 0.04f) * 100f) + " cm further out so that none starts inside the body.");
                    // Which way the triangles face: compare with the direction out from each piece's part.
                    float facing = 0f;
                    for (int t = 0; t < mesh.triangles.Count; t += 3)
                    {
                        int i0 = mesh.triangles[t], i1 = mesh.triangles[t + 1], i2 = mesh.triangles[t + 2];
                        Vector3 n = Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
                        facing += Vector3.Dot(n, outward[i0] + outward[i1] + outward[i2]);
                    }
                    result.flipTriangles = facing < 0f;
                    break;
                }
                gap += 0.02f;
            }
            return result;
        }

        /// <summary>A point of a piece wrapped on an upright elliptical cylinder (torso, head), and the direction out from it.</summary>
        private static Vector3 OnEllipse(ArcTable table, Vector2 center, Vector2 axes, PiecePlacement placement, float x, float y, float under, out Vector3 normal)
        {
            if (placement.flat)
            {
                float a0 = placement.angle * Mathf.Deg2Rad;
                normal = new Vector3(-Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                Vector3 across = Vector3.Cross(Vector3.down, normal);
                Vector3 rim = new Vector3(center.x - (axes.x - under) * Mathf.Sin(a0), y, center.y + (axes.y - under) * Mathf.Cos(a0));
                return rim + across * x;
            }
            float t = table.Advance(placement.angle * Mathf.Deg2Rad, x);
            normal = new Vector3(-Mathf.Sin(t), 0f, Mathf.Cos(t));
            return new Vector3(center.x - (axes.x - under) * Mathf.Sin(t), y, center.y + (axes.y - under) * Mathf.Cos(t));
        }

        /// <summary>
        /// A point of a piece wrapped on a round cylinder about <paramref name="axis"/> (pointing away from the
        /// piece's top), or laid flat on the plane touching it at <paramref name="angle"/>. Angle 0 faces
        /// <paramref name="reference"/>, a quarter turn faces <paramref name="side"/>.
        /// </summary>
        private static Vector3 OnCylinder(Vector3 center, Vector3 axis, Vector3 reference, Vector3 side, float radius, float angle, float x, bool flat, out Vector3 normal)
        {
            if (flat)
            {
                normal = reference * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                return center + normal * radius + Vector3.Cross(axis, normal) * x;
            }
            float t = angle + x / radius;
            normal = reference * Mathf.Cos(t) + side * Mathf.Sin(t);
            return center + normal * radius;
        }

        /// <summary>
        /// Where on the torso a leg piece facing <paramref name="legAngle"/> sits at the hips: the left
        /// leg's front and back halves cover the torso's left front and left back quarters, meeting the
        /// right leg's at the centre front and back (the crotch seams).
        /// </summary>
        public static float TorsoAngleForLeg(bool left, float legAngle)
        {
            // The leg's inner side is where the two quarters part: the left leg's inside is at -90, the right's at 90.
            float start = left ? -90f : -270f;
            float a = Mathf.Repeat(legAngle - start, 360f) + start;
            return (left ? 45f : -45f) + a * 0.5f;
        }

        /// <summary>Arc length along an ellipse x = -a sin t, z = b cos t, for wrapping pieces without stretching them.</summary>
        private sealed class ArcTable
        {
            private const int Steps = 4096;
            private const float Start = -2f * Mathf.PI;
            private const float End = 4f * Mathf.PI;
            private readonly float[] length = new float[Steps + 1];

            public ArcTable(float a, float b)
            {
                Vector2 previous = Point(Start, a, b);
                for (int i = 1; i <= Steps; i++)
                {
                    Vector2 p = Point(T(i), a, b);
                    length[i] = length[i - 1] + (p - previous).magnitude;
                    previous = p;
                }
            }

            private static float T(int i) => Start + (End - Start) * i / Steps;
            private static Vector2 Point(float t, float a, float b) => new Vector2(-a * Mathf.Sin(t), b * Mathf.Cos(t));

            private float LengthAt(float t)
            {
                float f = Mathf.Clamp((t - Start) / (End - Start) * Steps, 0f, Steps);
                int i = Mathf.Min(Steps - 1, (int)f);
                return Mathf.Lerp(length[i], length[i + 1], f - i);
            }

            /// <summary>The parameter reached by walking <paramref name="distance"/> along the ellipse from <paramref name="t0"/>.</summary>
            public float Advance(float t0, float distance)
            {
                float target = LengthAt(t0) + distance;
                int lo = 0, hi = Steps;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (length[mid] < target) lo = mid; else hi = mid;
                }
                float span = length[hi] - length[lo];
                float s = span > 1e-9f ? (target - length[lo]) / span : 0f;
                return Mathf.Lerp(T(lo), T(hi), s);
            }
        }
    }

    /// <summary>How well a drape fits: open seams, cloth in the body, stretch and pins that slipped.</summary>
    public struct FitMetrics
    {
        public float maxStitchGapMm, meanStitchGapMm;
        public int particlesInBody;
        public int particlesInsideClearance;
        public float deepestMm;
        public float maxStretchPercent, meanStretchPercent;
        public float maxPinDriftMm;
        public int particles;

        public static FitMetrics Measure(ClothModel model, Vector3[] x, BodyCollider body, float clearance, IList<SolverPin> pins)
        {
            var m = new FitMetrics { particles = x.Length };
            int stitches = 0, stretches = 0;
            double stitchSum = 0, stretchSum = 0;
            foreach (ClothConstraint c in model.Constraints)
            {
                float len = Vector3.Distance(x[c.a], x[c.b]);
                if (c.kind == (int)ConstraintKind.Stitch)
                {
                    stitches++; stitchSum += len;
                    m.maxStitchGapMm = Mathf.Max(m.maxStitchGapMm, len * 1000f);
                }
                else if (c.kind == (int)ConstraintKind.Stretch && c.rest > 1e-6f)
                {
                    float strain = Mathf.Max(0f, (len - c.rest) / c.rest) * 100f;
                    stretches++; stretchSum += strain;
                    m.maxStretchPercent = Mathf.Max(m.maxStretchPercent, strain);
                }
            }
            m.meanStitchGapMm = stitches > 0 ? (float)(stitchSum / stitches * 1000.0) : 0f;
            m.meanStretchPercent = stretches > 0 ? (float)(stretchSum / stretches) : 0f;
            // The body distances are most of the cost on fine cloth: search near each point first, in parallel.
            var distance = new float[x.Length];
            float near = Mathf.Clamp(clearance * 2f, 0.005f, 0.02f);
            System.Threading.Tasks.Parallel.For(0, x.Length, i =>
                distance[i] = body.ClosestNear(x[i], near, out _, out _, out float sd) ? sd : float.MaxValue);
            for (int i = 0; i < x.Length; i++)
            {
                float d = distance[i];
                if (d == float.MaxValue) continue;
                if (d < 0f) { m.particlesInBody++; m.deepestMm = Mathf.Max(m.deepestMm, -d * 1000f); }
                if (d < clearance * 0.9f) m.particlesInsideClearance++;
            }
            if (pins != null)
            {
                foreach (SolverPin pin in pins)
                {
                    if (pin.weight >= 0.999f) m.maxPinDriftMm = Mathf.Max(m.maxPinDriftMm, Vector3.Distance(x[pin.particle], pin.target) * 1000f);
                }
            }
            return m;
        }

        public override string ToString() =>
            $"seam gap max {maxStitchGapMm:0.00} mm (mean {meanStitchGapMm:0.00}), in body {particlesInBody} (deepest {deepestMm:0.0} mm), within clearance {particlesInsideClearance}, stretch max {maxStretchPercent:0.0}% (mean {meanStretchPercent:0.00}%), pin drift {maxPinDriftMm:0.00} mm";
    }
}
