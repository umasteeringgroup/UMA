using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Turns unrolled pieces (<see cref="RegionUnfold"/>) into pattern data: each outline simplified into points (kept
    /// within <see cref="Tolerance"/> of the flattened outline, with a corner wherever what an edge is sewn to changes),
    /// openings where the region ended, seams wherever two outlines share body vertices (two pieces, or the two sides of
    /// a cut), and a placement round the part that lays each piece where it was painted.
    /// </summary>
    public static class RegionPieces
    {
        /// <summary>
        /// How far a simplified outline may stray from the smoothed, flattened one (cm). The body's triangles make the
        /// region's edges ragged by about half a centimetre; this smooths that out.
        /// </summary>
        public const float Tolerance = 0.5f;

        private sealed class Work
        {
            public FlatPiece Flat;
            public PatternPiece Piece;
            public List<Vector2> Points;     // outline (cm), rotated so index 0 starts a run
            public List<int> Ids;
            public int[] PartnerPiece, PartnerEdge;   // per outline edge i (i to i+1): the other piece and its edge, or -1
            public float[] Along;            // length along the outline to each point (cm)
            public List<int> Kept;           // outline indices that became pattern points, in order
            public int[] EdgeOf;             // per outline edge: the pattern edge it is part of
            public bool[] RunStart;          // per outline edge: what it is sewn to is not the edge before carried on
        }

        /// <summary>
        /// Which outline edges start a run: an edge sewn to something else than the edge before it (another piece, an
        /// opening), or to the same piece but not where the edge before it left off (the other side runs backwards).
        /// </summary>
        private static bool[] RunStarts(Work w, List<Work> all)
        {
            int n = w.Ids.Count;
            var start = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                if (w.PartnerPiece[i] != w.PartnerPiece[prev]) { start[i] = true; continue; }
                if (w.PartnerPiece[i] < 0) continue;
                int m = all[w.PartnerPiece[i]].Ids.Count;
                start[i] = w.PartnerEdge[prev] != (w.PartnerEdge[i] + 1) % m;
            }
            return start;
        }

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>
        /// Adds the pieces to a pattern, laid out on the canvas after the pieces already there, each scaled by
        /// 1 + <paramref name="ease"/>. Returns the pieces added.
        /// </summary>
        public static List<PatternPiece> AddToPattern(ClothesPattern pattern, List<FlatPiece> flats, BodyLandmarks marks, float ease, string fabric, string race = null)
        {
            var work = new List<Work>();
            foreach (FlatPiece f in flats) if (f != null && f.Outline.Count >= 3) work.Add(new Work { Flat = f, Points = new List<Vector2>(f.Outline), Ids = new List<int>(f.OutlineIds) });
            // Which outline edge meets which: the same two body vertices, run the other way.
            var byKey = new Dictionary<long, List<(int piece, int edge)>>();
            for (int p = 0; p < work.Count; p++)
            {
                int n = work[p].Ids.Count;
                for (int i = 0; i < n; i++)
                {
                    long key = Key(work[p].Ids[i], work[p].Ids[(i + 1) % n]);
                    if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<(int, int)>(2);
                    list.Add((p, i));
                }
            }
            for (int p = 0; p < work.Count; p++)
            {
                Work w = work[p];
                int n = w.Ids.Count;
                w.PartnerPiece = new int[n]; w.PartnerEdge = new int[n];
                for (int i = 0; i < n; i++)
                {
                    w.PartnerPiece[i] = -1; w.PartnerEdge[i] = -1;
                    // An edge whose two ends are one body vertex (a cut's tip) is no seam.
                    if (w.Ids[i] == w.Ids[(i + 1) % n]) continue;
                    foreach (var other in byKey[Key(w.Ids[i], w.Ids[(i + 1) % n])])
                    {
                        if (other.piece == p && other.edge == i) continue;
                        w.PartnerPiece[i] = other.piece; w.PartnerEdge[i] = other.edge;
                        break;
                    }
                }
            }
            // Start each outline at a run's start, so no run wraps round index 0.
            foreach (Work w in work) RotateToRunStart(w, work);
            foreach (Work w in work) w.RunStart = RunStarts(w, work);

            float x = 0f;
            foreach (PatternPiece existing in pattern.pieces) if (existing != null && existing.points.Count > 0) x = Mathf.Max(x, existing.Bounds().xMax + 8f);
            var added = new List<PatternPiece>();
            for (int p = 0; p < work.Count; p++)
            {
                Work w = work[p];
                // Ease about the middle, then onto the canvas (the source triangles with it).
                Vector2 middle = Vector2.zero;
                foreach (Vector2 v in w.Points) middle += v;
                middle /= w.Points.Count;
                for (int i = 0; i < w.Points.Count; i++) w.Points[i] = (w.Points[i] - middle) * (1f + ease);
                Rect bounds = BoundsOf(w.Points);
                Vector2 place = new Vector2(x - bounds.xMin, -bounds.yMin);
                for (int i = 0; i < w.Points.Count; i++) w.Points[i] += place;
                x += bounds.width + 8f;
                SmoothRuns(w);
                w.Along = new float[w.Points.Count + 1];
                for (int i = 0; i < w.Points.Count; i++) w.Along[i + 1] = w.Along[i] + (w.Points[(i + 1) % w.Points.Count] - w.Points[i]).magnitude;
                w.Kept = Simplify(w);
                w.Piece = MakePiece(pattern, w, work, fabric);
                w.Piece.placement = Placement(w.Flat, marks);
                if (!string.IsNullOrEmpty(race) && w.Flat.SourceFlat != null && w.Piece.points.Count > 0)
                {
                    var flat = new Vector2[w.Flat.SourceFlat.Length];
                    for (int i = 0; i < flat.Length; i++) flat[i] = (w.Flat.SourceFlat[i] - middle) * (1f + ease) + place;
                    w.Piece.bodyLayout = new BodyLayout
                    {
                        race = race,
                        anchorPointId = w.Piece.points[0].id,
                        anchor = w.Piece.points[0].position,
                        flat = flat,
                        positions = (Vector3[])w.Flat.Source3D.Clone(),
                        normals = (Vector3[])w.Flat.SourceNormals.Clone(),
                        triangles = (int[])w.Flat.SourceTriangles.Clone()
                    };
                }
                pattern.pieces.Add(w.Piece);
                added.Add(w.Piece);
            }
            // Seams, once per pair of runs.
            for (int p = 0; p < work.Count; p++) AddSeams(pattern, work, p);
            return added;
        }

        private static Rect BoundsOf(List<Vector2> points)
        {
            Rect r = new Rect(points[0], Vector2.zero);
            foreach (Vector2 v in points) { r.xMin = Mathf.Min(r.xMin, v.x); r.xMax = Mathf.Max(r.xMax, v.x); r.yMin = Mathf.Min(r.yMin, v.y); r.yMax = Mathf.Max(r.yMax, v.y); }
            return r;
        }

        private static void RotateToRunStart(Work w, List<Work> all)
        {
            int n = w.Ids.Count, start = -1;
            bool[] starts = RunStarts(w, all);
            for (int i = 0; i < n && start < 0; i++) if (starts[i]) start = i;
            if (start <= 0) return;
            Rotate(w.Points, start); Rotate(w.Ids, start); Rotate(w.Flat.Outline3D.Count == n ? w.Flat.Outline3D : null, start);
            w.PartnerPiece = RotateArray(w.PartnerPiece, start);
            w.PartnerEdge = RotateArray(w.PartnerEdge, start);
            // Every edge that pointed at one of this outline's edges must follow the renumbering.
            foreach (Work other in all)
            {
                if (other.PartnerPiece == null) continue;
                for (int i = 0; i < other.PartnerPiece.Length; i++)
                {
                    if (other.PartnerPiece[i] >= 0 && all[other.PartnerPiece[i]] == w) other.PartnerEdge[i] = (other.PartnerEdge[i] - start + n) % n;
                }
            }
        }

        private static void Rotate<T>(List<T> list, int start)
        {
            if (list == null) return;
            var copy = new List<T>(list);
            for (int i = 0; i < list.Count; i++) list[i] = copy[(i + start) % copy.Count];
        }

        private static int[] RotateArray(int[] a, int start)
        {
            var r = new int[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[(i + start) % a.Length];
            return r;
        }

        /// <summary>Smooths each run of the outline (its ends stay where they are): three passes of a quarter-half-quarter average.</summary>
        private static void SmoothRuns(Work w)
        {
            int n = w.Points.Count;
            for (int pass = 0; pass < 3; pass++)
            {
                var next = new List<Vector2>(w.Points);
                for (int i = 0; i < n; i++)
                {
                    // A run's start (and so its neighbour's end) is a corner: it stays.
                    if (w.RunStart[i]) continue;
                    next[i] = w.Points[(i - 1 + n) % n] * 0.25f + w.Points[i] * 0.5f + w.Points[(i + 1) % n] * 0.25f;
                }
                w.Points.Clear();
                w.Points.AddRange(next);
            }
        }

        /// <summary>The outline points kept: every run's start, and within runs what Douglas-Peucker keeps.</summary>
        private static List<int> Simplify(Work w)
        {
            int n = w.Points.Count;
            var keep = new bool[n];
            keep[0] = true;
            for (int i = 1; i < n; i++) if (w.RunStart[i]) keep[i] = true;
            var corners = new List<int>();
            for (int i = 0; i < n; i++) if (keep[i]) corners.Add(i);
            // At least three points, spread round, so even a plain opening is a piece.
            if (corners.Count < 3)
            {
                keep[n / 3] = true; keep[2 * n / 3] = true;
                corners.Clear();
                for (int i = 0; i < n; i++) if (keep[i]) corners.Add(i);
            }
            for (int c = 0; c < corners.Count; c++)
            {
                int a = corners[c], b = c + 1 < corners.Count ? corners[c + 1] : n;
                DouglasPeucker(w.Points, a, b, keep);
            }
            var kept = new List<int>();
            for (int i = 0; i < n; i++) if (keep[i]) kept.Add(i);
            return kept;
        }

        private static void DouglasPeucker(List<Vector2> points, int a, int b, bool[] keep)
        {
            int n = points.Count;
            if (b - a < 2) return;
            Vector2 pa = points[a % n], pb = points[b % n];
            float worst = 0f;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = DistanceToSegment(points[i % n], pa, pb);
                if (d > worst) { worst = d; at = i; }
            }
            if (worst <= Tolerance || at < 0) return;
            keep[at % n] = true;
            DouglasPeucker(points, a, at, keep);
            DouglasPeucker(points, at, b, keep);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        private static PatternPiece MakePiece(ClothesPattern pattern, Work w, List<Work> all, string fabric)
        {
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = UniqueName(pattern, w.Flat.Name), fabric = FabricPresets.Create(fabric) };
            int n = w.Points.Count;
            w.EdgeOf = new int[n];
            for (int k = 0; k < w.Kept.Count; k++)
            {
                int a = w.Kept[k], b = k + 1 < w.Kept.Count ? w.Kept[k + 1] : n;
                piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = w.Points[a] });
                int partner = w.PartnerPiece[a];
                string name = partner < 0 ? "Opening " + (k + 1) : partner == all.IndexOf(w) ? "Cut " + (k + 1) : "To " + all[partner].Flat.Name + " " + (k + 1);
                piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId(), name = name, opening = partner < 0 });
                for (int i = a; i < b; i++) w.EdgeOf[i] = k;
            }
            return piece;
        }

        private static string UniqueName(ClothesPattern pattern, string name)
        {
            string candidate = name;
            for (int k = 2; pattern.pieces.Exists(p => p != null && p.name == candidate); k++) candidate = name + " (" + k + ")";
            return candidate;
        }

        /// <summary>The pattern edge an outline point starts, and how far along the pattern edge an outline point is (0 to 1).</summary>
        private static float FractionAlong(Work w, int edge, int point)
        {
            int a = w.Kept[edge], b = edge + 1 < w.Kept.Count ? w.Kept[edge + 1] : w.Points.Count;
            float length = w.Along[b] - w.Along[a];
            return length > 1e-6f ? Mathf.Clamp01((w.Along[point] - w.Along[a]) / length) : 0f;
        }

        /// <summary>
        /// Sews this piece's pattern edges to the other side's, outline edge by outline edge: each stretch of an edge
        /// that lies on one of the other side's pattern edges becomes one seam span, start to end the other way round.
        /// Done once per pair: from the piece with the lower index (or, sewn to itself, from the earlier outline edge).
        /// </summary>
        private static void AddSeams(ClothesPattern pattern, List<Work> all, int p)
        {
            Work w = all[p];
            int n = w.Points.Count;
            int i = 0;
            while (i < n)
            {
                int q = w.PartnerPiece[i];
                if (q < 0 || q < p || (q == p && w.PartnerEdge[i] < i)) { i++; continue; }
                Work o = all[q];
                int m = o.Points.Count;
                // The stretch: outline edges i..j-1 here, on one pattern edge here and one there.
                int edgeHere = w.EdgeOf[i], edgeThere = o.EdgeOf[w.PartnerEdge[i]];
                int j = i + 1;
                while (j < n && w.PartnerPiece[j] == q && w.EdgeOf[j] == edgeHere && o.EdgeOf[w.PartnerEdge[j]] == edgeThere && w.PartnerEdge[j] == (w.PartnerEdge[j - 1] - 1 + m) % m) j++;
                // Here the stretch runs from point i to point j; there from point (partner of j-1) to point (partner of i) + 1.
                int thereStart = w.PartnerEdge[j - 1], thereEnd = w.PartnerEdge[i] + 1;
                float a0 = FractionAlong(w, edgeHere, i), a1 = FractionAlong(w, edgeHere, j);
                float b0 = FractionAlong(o, edgeThere, thereStart), b1 = FractionAlong(o, edgeThere, thereEnd);
                if (a1 - a0 > 1e-4f && b1 - b0 > 1e-4f)
                {
                    pattern.seams.Add(new PatternSeam
                    {
                        id = ClothesPattern.NewId(),
                        a = new SeamSpan(w.Piece.id, w.Piece.edges[edgeHere].id, a0, a1),
                        b = new SeamSpan(o.Piece.id, o.Piece.edges[edgeThere].id, b0, b1),
                        reversed = true
                    });
                }
                i = j;
            }
        }

        /// <summary>Where a piece is laid: round its part, facing the way it was painted, its top where it was.</summary>
        public static PiecePlacement Placement(FlatPiece flat, BodyLandmarks marks)
        {
            RegionPatch frame = flat.Frame;
            var placement = new PiecePlacement { part = flat.Part, distanceCm = 0f };
            Vector3 up = frame.Up.normalized;
            Vector3 front = Vector3.ProjectOnPlane(frame.Front, up).normalized;
            Vector3 left = Vector3.ProjectOnPlane(frame.Ninety, up).normalized;
            Vector3 d = Vector3.ProjectOnPlane(flat.Centre - frame.AxisPoint, up);
            // A piece that went all the way round faces away from its cut.
            if (flat.WentRound || d.sqrMagnitude < 1e-6f) d = -Vector3.ProjectOnPlane(frame.CutSide, up);
            placement.angle = Mathf.Atan2(Vector3.Dot(d, left), Vector3.Dot(d, front)) * Mathf.Rad2Deg;
            float top = float.MinValue, nearest = float.MaxValue;
            foreach (Vector3 p in flat.Outline3D)
            {
                top = Mathf.Max(top, p.y);
                nearest = Mathf.Min(nearest, Vector3.Dot(p - frame.AxisPoint, -up));
            }
            switch (flat.Part)
            {
                case BodyPart.Head: placement.topAboveShoulderCm = (top - marks.headTopY) * 100f; break;
                case BodyPart.LeftArm:
                case BodyPart.RightArm: placement.topAboveShoulderCm = -nearest * 100f; break;
                case BodyPart.LeftLeg:
                case BodyPart.RightLeg: placement.topAboveShoulderCm = (top - marks.waistY) * 100f; break;
                default: placement.topAboveShoulderCm = (top - marks.shoulderTopY) * 100f; break;
            }
            return placement;
        }
    }
}
