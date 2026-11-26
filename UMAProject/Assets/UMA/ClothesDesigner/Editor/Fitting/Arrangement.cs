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
            /// <summary>How many gaps the search for one that keeps every point out of the body tried (1 at a fixed gap).</summary>
            public int gapsTried;
            public Vector2 semiAxes;
            public readonly List<string> messages = new List<string>();
            /// <summary>
            /// Per piece, the rings of the shape it was laid on, from its top down, one per centimetre of it;
            /// empty for a piece laid flat. For drawing the layout guide and its handles.
            /// </summary>
            public GuideRing[][] guides;
        }

        /// <summary>
        /// One ring of the shape a piece is wrapped on (an oval round the torso or head, a circle round an
        /// arm or leg), at one row of the piece. <see cref="Point"/> at the piece's angle is where its middle lies.
        /// </summary>
        public struct GuideRing
        {
            public Vector3 center;
            /// <summary>Along the part, away from the piece's top.</summary>
            public Vector3 axis;
            /// <summary>Angle 0 (the front; the top of an arm) and a quarter turn on.</summary>
            public Vector3 up, side;
            /// <summary>Radii towards <see cref="up"/> and <see cref="side"/> (m).</summary>
            public float a, b;
            /// <summary>How far down the piece the ring is, from its top (cm).</summary>
            public float downCm;

            public Vector3 Point(float t) => center + up * (a * Mathf.Cos(t)) + side * (b * Mathf.Sin(t));
            /// <summary>The direction out from the ring at an angle.</summary>
            public Vector3 Out(float t) => (up * (Mathf.Cos(t) / Mathf.Max(1e-4f, a)) + side * (Mathf.Sin(t) / Mathf.Max(1e-4f, b))).normalized;
            /// <summary>The angle of a point round the ring (its projection onto the ring's plane).</summary>
            public float AngleOf(Vector3 p)
            {
                Vector3 d = p - center;
                return Mathf.Atan2(Vector3.Dot(d, side) / Mathf.Max(1e-4f, b), Vector3.Dot(d, up) / Mathf.Max(1e-4f, a));
            }
        }

        /// <summary>
        /// The ring the Faces, Top and Distance handles sit on: a quarter of the way down the piece, where its
        /// middle has cloth (the top ring may be above a neckline or a sleeve cap's curve).
        /// </summary>
        public static GuideRing HandleRing(GuideRing[] rings) => rings[rings.Length / 4];

        /// <summary>The angle round its guide rings (radians) at which a piece's middle lies: its Faces value, mirrored on the left arm.</summary>
        public static float GuideAngle(PiecePlacement placement) =>
            (placement.part == BodyPart.LeftArm ? -placement.angle : placement.angle) * Mathf.Deg2Rad;

        /// <summary>A leg piece goes from the torso's cylinder to the leg's over at least this height above the crotch.</summary>
        public const float LegBlendCm = 12f;
        /// <summary>How far out from the body the pieces start, before any are moved out to clear it (m).</summary>
        public const float DefaultGapM = 0.04f;
        /// <summary>
        /// How far a sleeve starts from the arm's surface (m), where it is not cut wider. Closer than the
        /// torso's gap: a sleeve laid farther out covers less of the way round, and its underarm seam then
        /// closes above the arm instead of under it.
        /// </summary>
        public const float ArmGapM = 0.015f;
        /// <summary>
        /// How far out from where it was painted a piece made from a body region starts (its source already lies the
        /// clearance out from the body): it has the body's shape, so it needs no more room to fall into place.
        /// </summary>
        public const float BodyLaidGapM = 0.004f;
        /// <summary>
        /// When a sleeve's edges would meet higher than this above the arm's axis (in arm radii) near its
        /// far end, its seam is likely to close above the arm: the arrangement says so.
        /// </summary>
        public const float HighestSeamClose = 0.5f;
        /// <summary>The most a limb's tube can be tilted, either way (degrees).</summary>
        public const float MaxTiltDeg = 60f;
        /// <summary>The rows the width of arm pieces is added up in (cm along the arm), and the first row's place.</summary>
        private const int ArmRowCount = 200;
        private const int ArmRowStartCm = -40;

        private static readonly Vector3 Front = Vector3.forward;
        /// <summary>The body's left: the renderer's -x, as the torso's 90 degrees.</summary>
        private static readonly Vector3 BodyLeft = Vector3.left;

        /// <summary>How far outside the cloth under it a free piece (a belt over a dress) is laid out (cm).</summary>
        public const float FreePieceGapCm = 1f;

        /// <summary>
        /// For each free piece laid round the torso (<see cref="ClothModel.FreePieces"/>: a belt over a dress), how much
        /// farther out (cm) it has to be laid to start <see cref="FreePieceGapCm"/> outside every other piece's laid-out
        /// cloth beside it, at its height and angle. Laid that much farther out it keeps its own length, so it covers less
        /// of the way round and its seams start open; they close late (<see cref="ConstraintKind.LateStitch"/>), tightening
        /// it round the cloth under it. Null when no piece needs moving.
        /// </summary>
        public static float[] FreePieceClearance(ClothesPattern pattern, ClothesFit fit, PatternMesh mesh, Vector3[] positions, BodyReference body)
        {
            bool[] free = ClothModel.FreePieces(pattern);
            bool any = false;
            for (int p = 0; p < free.Length; p++)
            {
                if (!free[p]) continue;
                if (fit.PlacementFor(pattern.pieces[p]).part != BodyPart.Torso) free[p] = false;
                else any = true;
            }
            if (!any || positions == null || positions.Length != mesh.VertexCount) return null;
            Vector2 c = body.Landmarks.torsoCenter;
            float Radius(Vector3 q) => new Vector2(q.x - c.x, q.z - c.y).magnitude;
            float Angle(Vector3 q) => Mathf.Atan2(q.z - c.y, q.x - c.x) * Mathf.Rad2Deg;
            var extra = new float[pattern.pieces.Count];
            bool moved = false;
            for (int i = 0; i < positions.Length; i++)
            {
                int p = mesh.piece[i];
                if (p < 0 || p >= free.Length || !free[p]) continue;
                float r = Radius(positions[i]), a = Angle(positions[i]), outer = 0f;
                for (int j = 0; j < positions.Length; j++)
                {
                    int q = mesh.piece[j];
                    if (q >= 0 && q < free.Length && free[q]) continue;
                    if (Mathf.Abs(positions[j].y - positions[i].y) > 0.03f || Mathf.Abs(Mathf.DeltaAngle(a, Angle(positions[j]))) > 15f) continue;
                    outer = Mathf.Max(outer, Radius(positions[j]));
                }
                float need = (outer + FreePieceGapCm * 0.01f - r) * 100f;
                if (need > extra[p]) { extra[p] = need; moved = true; }
            }
            return moved ? extra : null;
        }

        /// <param name="fixedGap">
        /// For a live preview while a piece is dragged: use this gap and skip the check that no point
        /// starts in the body (which may push every piece farther out). Null for the full arrangement.
        /// </param>
        /// <param name="extraDistanceCm">Per piece, added to its placement's Distance (see <see cref="FreePieceClearance"/>), or null.</param>
        public static Result Arrange(ClothesPattern pattern, ClothesFit fit, PatternMesh mesh, BodyReference body, float? fixedGap = null, float[] extraDistanceCm = null)
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
            var piecePlacement = new PiecePlacement[pieces];
            var part = new BodyPart[pieces];
            bool fellBack = false;
            for (int p = 0; p < pieces; p++)
            {
                piecePlacement[p] = fit.PlacementFor(pattern.pieces[p]);
                part[p] = piecePlacement[p].part;
                if (part[p] != BodyPart.Torso && !marks.limbs) { part[p] = BodyPart.Torso; fellBack = true; }
            }
            // A second ply (Duplicate Sewn Round: a copy on another layer) is placed as its original is, moved with it.
            var plyOf = new int[pieces];
            for (int p = 0; p < pieces; p++)
            {
                plyOf[p] = -1;
                PatternPiece ply = pattern.pieces[p];
                if (ply == null || string.IsNullOrEmpty(ply.duplicateOf)) continue;
                int o = pattern.IndexOfPiece(ply.duplicateOf);
                if (o < 0 || o == p || pattern.pieces[o] == null || pattern.pieces[o].layer == ply.layer) continue;
                plyOf[p] = o;
                piecePlacement[p] = piecePlacement[o];
                part[p] = part[o];
            }
            // Pieces with the same placement that touch on the canvas (the parts of a piece that was cut)
            // are laid out as the piece they were cut from: one top and one middle for them all.
            var minY = new float[pieces];
            for (int p = 0; p < pieces; p++) minY[p] = float.MaxValue;
            for (int v = 0; v < mesh.VertexCount; v++) minY[mesh.piece[v]] = Mathf.Min(minY[mesh.piece[v]], mesh.layout[v].y);
            // Each piece's own extent on the canvas, as laid out (sizes applied), before groups share theirs.
            var ownMinX = (float[])minX.Clone();
            var ownMinY = (float[])minY.Clone();
            var groupOf = new int[pieces];
            for (int p = 0; p < pieces; p++) groupOf[p] = p;
            int Root(int p) { while (groupOf[p] != p) p = groupOf[p] = groupOf[groupOf[p]]; return p; }
            const float touch = 0.5f;
            for (int p = 0; p < pieces; p++)
            {
                if (top[p] == float.MinValue) continue;
                for (int q = p + 1; q < pieces; q++)
                {
                    if (top[q] == float.MinValue || !piecePlacement[p].SameAs(piecePlacement[q])) continue;
                    bool across = minX[p] <= maxX[q] + touch && minX[q] <= maxX[p] + touch;
                    bool along = minY[p] <= top[q] + touch && minY[q] <= top[p] + touch;
                    if (across && along) groupOf[Root(q)] = Root(p);
                }
            }
            for (int p = 0; p < pieces; p++)
            {
                int r = Root(p);
                if (r == p) continue;
                top[r] = Mathf.Max(top[r], top[p]); minX[r] = Mathf.Min(minX[r], minX[p]); maxX[r] = Mathf.Max(maxX[r], maxX[p]);
                minY[r] = Mathf.Min(minY[r], minY[p]);
            }
            for (int p = 0; p < pieces; p++) { int r = Root(p); top[p] = top[r]; minX[p] = minX[r]; maxX[p] = maxX[r]; minY[p] = minY[r]; }
            // A second ply (Duplicate Sewn Round: a copy on another layer) is laid on its original, wherever it lies on the
            // canvas: the original's top and middle, moved by how far apart the two lie there. The layers then part them.
            for (int p = 0; p < pieces; p++)
            {
                int o = plyOf[p];
                if (o < 0 || top[p] == float.MinValue || top[o] == float.MinValue) continue;
                var shift = new Vector2(ownMinX[p] - ownMinX[o], ownMinY[p] - ownMinY[o]);
                top[p] = top[o] + shift.y; minX[p] = minX[o] + shift.x; maxX[p] = maxX[o] + shift.x; minY[p] = minY[o] + shift.y;
            }
            Dictionary<(BodyPart, int), float[]> armRows = ArmRowWidths(mesh, piecePlacement, part, top);
            for (int p = 0; p < pieces; p++) centerX[p] = (minX[p] + maxX[p]) * 0.5f;
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

            // Pieces made from a painted body region are laid where they were painted, on the race they were painted on.
            var bodyLaid = new BodyLayoutMap[pieces];
            string raceName = fit != null && fit.race != null ? fit.race.raceName : null;
            for (int p = 0; p < pieces; p++) bodyLaid[p] = BodyLayoutMap.For(pattern.pieces[p], raceName);

            Vector2 half = body.HalfExtents(lowest, marks.shoulderTopY - 0.05f);
            Vector2 center = marks.torsoCenter;
            float clearance = fit.clearanceCm * 0.01f;
            float gap = fixedGap ?? DefaultGapM;
            // The search keeps the layout with the fewest points inside the body (at the smallest gap) and moves on while
            // that keeps falling. Two gaps in a row that free no more points end it: those points are ones the gap does
            // not move (a piece held in by a negative Distance stays as close at every gap), and trying all eight gaps
            // took 0.9 s on a large dress. Every sample, on Human Female and Male 3.0, clears in at most seven gaps with
            // the count falling all the way (one pause of a single gap: the Hoodie on the male, 14 then 14 then 12), so
            // their layouts are as before.
            int bestInside = int.MaxValue, bestAttempt = 0, notFewer = 0;
            float bestGap = gap;
            Vector3[] bestPositions = null, bestOutward = null;
            Dictionary<int, GuideRing>[] bestRings = null;
            float[] bestWrap = null, bestCloses = null;
            int[] bestPerPiece = null;
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
                // Per arm piece: the least share of the way round the arm it covers near its far end.
                var wrapAtEnd = new float[pieces];
                // Per arm piece: how high above the arm's axis its seam would close near the far end, in arm radii.
                var closesAt = new float[pieces];
                for (int p = 0; p < pieces; p++) { wrapAtEnd[p] = float.MaxValue; closesAt[p] = float.MinValue; }
                var rings = new Dictionary<int, GuideRing>[pieces];
                for (int p = 0; p < pieces; p++) rings[p] = new Dictionary<int, GuideRing>();
                // The first point of each centimetre row of a piece that is wrapped (not flat, not a folded layer) records its ring.
                void Ring(int p, float under, float downM, Vector3 ringCenter, Vector3 ringAxis, Vector3 ringUp, Vector3 ringSide, float ra, float rb)
                {
                    if (piecePlacement[p].flat || under > 0f) return;
                    int row = Mathf.RoundToInt(downM * 100f);
                    if (rings[p].ContainsKey(row)) return;
                    rings[p][row] = new GuideRing { center = ringCenter, axis = ringAxis, up = ringUp, side = ringSide, a = ra, b = rb, downCm = row };
                }
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    int p = mesh.piece[v];
                    if (bodyLaid[p] != null)
                    {
                        // Where it was painted (its source lies the clearance out from the body), a little further out.
                        Vector3 onBody = bodyLaid[p].Position(mesh.drawn[v], out Vector3 bodyNormal);
                        positions[v] = onBody + bodyNormal * BodyLaidGapM;
                        outward[v] = bodyNormal;
                        continue;
                    }
                    PiecePlacement placement = piecePlacement[p];
                    float x = (mesh.layout[v].x - centerX[p] + placement.acrossCm) * 0.01f;
                    float down = (top[p] - mesh.layout[v].y) * 0.01f;
                    // Limbs: the distance can change evenly from the piece's top to its far end.
                    float height = top[p] - minY[p];
                    float towardsEnd = height > 1e-4f ? Mathf.Clamp01((top[p] - mesh.layout[v].y) / height) : 0f;
                    float distance = placement.distanceCm + (PiecePlacement.IsLimb(part[p]) ? placement.endDistanceCm * towardsEnd : 0f)
                        + (extraDistanceCm != null && p < extraDistanceCm.Length ? extraDistanceCm[p] : 0f);
                    float extra = Mathf.Max(-gap + 0.005f, distance * 0.01f);
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
                            Ring(p, under, down, new Vector3(marks.headCenter.x, y, marks.headCenter.y), Vector3.down, Front, BodyLeft, axes.y, axes.x);
                            break;
                        }
                        case BodyPart.LeftArm:
                        case BodyPart.RightArm:
                        {
                            bool left = part[p] == BodyPart.LeftArm;
                            marks.ArmFrame(left, out Vector3 root, out Vector3 axis, out Vector3 up, out Vector3 side);
                            // 90 is the front of either arm; the left arm's frame turns the other way.
                            float angle = (left ? -placement.angle : placement.angle) * Mathf.Deg2Rad;
                            float along = down - placement.topAboveShoulderCm * 0.01f;
                            // A cone round the arm as it is (wide at the shoulder, narrow at the wrist), never
                            // closer than the clearance check allows, and never narrower than the sleeve is cut
                            // at that row, or the cloth would overlap round it.
                            float surface = marks.ArmSurfaceRadius(left, along);
                            float limbGap = ArmGapM + (gap - DefaultGapM);
                            float closest = clearance + 0.006f;
                            float armExtra = Mathf.Max(closest - limbGap, distance * 0.01f);
                            float rowWidth = RowWidth(armRows, part[p], placement, along);
                            float r = Mathf.Max(surface + limbGap + armExtra, rowWidth / (2f * Mathf.PI));
                            // Tilted, the tube turns about the shoulder joint (positive lowers its far end), but
                            // its middle moves off the arm's only as far as the arm still fits inside it: a
                            // loose sleeve can hang below the arm, a close one stays round it.
                            float tilt = Mathf.Clamp(placement.tiltDeg, -MaxTiltDeg, MaxTiltDeg) * Mathf.Deg2Rad;
                            float drop = Mathf.Clamp(along * Mathf.Sin(tilt), -Mathf.Max(0f, r - surface - closest), Mathf.Max(0f, r - surface - closest));
                            Vector3 c = root + axis * along - up * drop;
                            if (!placement.flat && rowWidth > 0f && towardsEnd >= 0.6f)
                            {
                                // Where the two edges meet when the seam closes: the middle of the chord between
                                // them, above the tube's middle when the piece covers less than half of the way
                                // round. Seen from the arm's axis, in arm radii.
                                float share = Mathf.Min(1f, rowWidth / (2f * Mathf.PI * r));
                                float meet = (r * Mathf.Cos(Mathf.PI * share) - drop) / Mathf.Max(0.005f, surface);
                                closesAt[p] = Mathf.Max(closesAt[p], meet);
                                wrapAtEnd[p] = Mathf.Min(wrapAtEnd[p], share);
                            }
                            position = OnCylinder(c, axis, up, side, Mathf.Max(0.01f, r - under), angle, x, placement.flat, out normal);
                            Ring(p, under, down, c, axis, up, side, r, r);
                            // The profile is the arm's alone: a point that still starts against the body (the
                            // thumb past the cuff, the side under the arm) is moved out on its own, rather than
                            // the check below moving every piece out.
                            for (int k = 0; k < 4 && !placement.flat; k++)
                            {
                                if (!body.Collider.Closest(position, out _, out _, out float d) || d >= closest) break;
                                position += normal * (closest - d + 0.001f);
                            }
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
                            // Tilted, the tube's far end moves out from the other leg (in, when negative), as far
                            // as the leg still fits inside it.
                            float tilt = Mathf.Clamp(placement.tiltDeg, -MaxTiltDeg, MaxTiltDeg) * Mathf.Deg2Rad;
                            float slack = Mathf.Max(0f, r - marks.legRadius - clearance - 0.006f);
                            float spread = Mathf.Clamp(down * Mathf.Tan(tilt), -slack, slack);
                            c += (left ? BodyLeft : -BodyLeft) * spread;
                            float angle = placement.angle * Mathf.Deg2Rad;
                            position = OnCylinder(c, Vector3.down, Front, BodyLeft, r, angle, x, placement.flat, out normal);
                            Ring(p, under, down, c, Vector3.down, Front, BodyLeft, r, r);
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
                            Ring(p, under, down, new Vector3(center.x, y, center.y), Vector3.down, Front, BodyLeft, axes.y, axes.x);
                            break;
                        }
                    }
                    positions[v] = position;
                    outward[v] = normal;
                }
                int inside = 0;
                var perPiece = new int[pieces];
                if (!fixedGap.HasValue)
                {
                    for (int v = 0; v < positions.Length; v++)
                    {
                        // A piece laid where it was painted sits at the clearance on purpose: only well inside it counts.
                        float limit = bodyLaid[mesh.piece[v]] != null ? clearance * 0.5f : clearance + 0.005f;
                        if (body.Collider.Closest(positions[v], out _, out _, out float d) && d < limit) { inside++; perPiece[mesh.piece[v]]++; }
                    }
                }
                if (inside < bestInside)
                {
                    bestInside = inside; bestAttempt = attempt; bestGap = gap; notFewer = 0;
                    bestPositions = positions; bestOutward = outward; bestRings = rings; bestWrap = wrapAtEnd; bestCloses = closesAt; bestPerPiece = perPiece;
                }
                else notFewer++;
                bool stuck = notFewer >= 2;
                if (inside == 0 || attempt == 7 || stuck)
                {
                    result.positions = bestPositions;
                    result.gap = bestGap;
                    result.gapsTried = attempt + 1;
                    result.semiAxes = new Vector2(half.x + bestGap, half.y + bestGap);
                    result.guides = new GuideRing[pieces][];
                    for (int p = 0; p < pieces; p++)
                    {
                        var list = new List<GuideRing>(bestRings[p].Values);
                        list.Sort((r0, r1) => r0.downCm.CompareTo(r1.downCm));
                        result.guides[p] = list.ToArray();
                    }
                    if (bestInside > 0)
                    {
                        var where = new List<string>();
                        for (int p = 0; p < pieces; p++) if (bestPerPiece[p] > 0) where.Add("'" + pattern.pieces[p].name + "' " + bestPerPiece[p]);
                        result.messages.Add(bestInside + " points of the cloth start inside or against the body (" + string.Join(", ", where) + ")."
                            + (stuck ? " Laying every piece further out did not free them: move those pieces out (their Distance) before simulating."
                                     : " Move the pieces further out before simulating."));
                    }
                    else if (bestAttempt > 0) result.messages.Add("The pieces were moved " + Mathf.RoundToInt((bestGap - DefaultGapM) * 100f) + " cm further out so that none starts inside the body.");
                    for (int p = 0; p < pieces; p++)
                    {
                        if (bestCloses[p] > HighestSeamClose)
                        {
                            result.messages.Add("'" + pattern.pieces[p].name + "' is laid round only " + Mathf.RoundToInt(bestWrap[p] * 100f)
                                + "% of the way round the arm near its far end, so its seam may close above the arm instead of under it. Bring its End Distance or Distance in, or tilt it down.");
                        }
                    }
                    // Which way the triangles face: compare with the direction out from each piece's part.
                    float facing = 0f;
                    for (int t = 0; t < mesh.triangles.Count; t += 3)
                    {
                        int i0 = mesh.triangles[t], i1 = mesh.triangles[t + 1], i2 = mesh.triangles[t + 2];
                        Vector3 n = Vector3.Cross(bestPositions[i1] - bestPositions[i0], bestPositions[i2] - bestPositions[i0]);
                        facing += Vector3.Dot(n, bestOutward[i0] + bestOutward[i1] + bestOutward[i2]);
                    }
                    result.flipTriangles = facing < 0f;
                    break;
                }
                gap += 0.02f;
            }
            return result;
        }

        /// <summary>
        /// How wide the arm pieces are, added up per centimetre along each arm (by their placement's top, not
        /// tilted): pieces on one arm at the same distance go round it together, like the halves of a
        /// two-piece sleeve. A piece set farther out (a cuff, a band) is counted on its own.
        /// </summary>
        private static Dictionary<(BodyPart, int), float[]> ArmRowWidths(PatternMesh mesh, PiecePlacement[] placements, BodyPart[] part, float[] top)
        {
            var result = new Dictionary<(BodyPart, int), float[]>();
            int pieces = placements.Length;
            var rowMin = new float[ArmRowCount];
            var rowMax = new float[ArmRowCount];
            for (int p = 0; p < pieces; p++)
            {
                if (!PiecePlacement.IsArm(part[p]) || placements[p].flat || top[p] == float.MinValue) continue;
                for (int i = 0; i < ArmRowCount; i++) { rowMin[i] = float.MaxValue; rowMax[i] = float.MinValue; }
                for (int v = mesh.pieceStart[p]; v < mesh.pieceStart[p] + mesh.pieceCount[p]; v++)
                {
                    int row = Mathf.FloorToInt(top[p] - mesh.layout[v].y - placements[p].topAboveShoulderCm) - ArmRowStartCm;
                    if (row < 0 || row >= ArmRowCount) continue;
                    rowMin[row] = Mathf.Min(rowMin[row], mesh.layout[v].x);
                    rowMax[row] = Mathf.Max(rowMax[row], mesh.layout[v].x);
                }
                var key = (part[p], Mathf.RoundToInt(placements[p].distanceCm * 10f));
                if (!result.TryGetValue(key, out float[] widths)) result[key] = widths = new float[ArmRowCount];
                // A centimetre's row of a coarser mesh can miss the outline's points: take each row's extent
                // with its neighbours'.
                for (int i = 0; i < ArmRowCount; i++)
                {
                    float lo = rowMin[i], hi = rowMax[i];
                    if (hi < lo) continue;
                    for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(ArmRowCount - 1, i + 1); j++)
                    {
                        if (rowMax[j] < rowMin[j]) continue;
                        lo = Mathf.Min(lo, rowMin[j]); hi = Mathf.Max(hi, rowMax[j]);
                    }
                    widths[i] += (hi - lo) * 0.01f;
                }
            }
            return result;
        }

        /// <summary>The arm pieces' added-up width (m) at a distance along the arm (m) from the shoulder joint.</summary>
        private static float RowWidth(Dictionary<(BodyPart, int), float[]> rows, BodyPart part, PiecePlacement placement, float along)
        {
            if (!rows.TryGetValue((part, Mathf.RoundToInt(placement.distanceCm * 10f)), out float[] widths)) return 0f;
            int row = Mathf.FloorToInt(along * 100f) - ArmRowStartCm;
            return row >= 0 && row < ArmRowCount ? widths[row] : 0f;
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
                if (c.kind == (int)ConstraintKind.Stitch || c.kind == (int)ConstraintKind.LateStitch)
                {
                    // Layers of a pleat sewn into a seam lie stacked: the spacing between them is not an open seam.
                    if (model.LayerDepth != null && c.a < model.LayerDepth.Length && c.b < model.LayerDepth.Length) len = Mathf.Max(0f, len - Mathf.Abs(model.LayerDepth[c.a] - model.LayerDepth[c.b]));
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
