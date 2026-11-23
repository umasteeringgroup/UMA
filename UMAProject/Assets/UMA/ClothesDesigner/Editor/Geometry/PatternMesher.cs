using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>The cloth mesh made from a pattern: flat positions, triangles, UVs and the stitch pairs of every seam, dart and pleat.</summary>
    public sealed class PatternMesh
    {
        /// <summary>Flat position of each vertex after the dimensions are applied (cm).</summary>
        public readonly List<Vector2> flat = new List<Vector2>();
        /// <summary>Flat position of each vertex on the drawn pattern, before the dimensions (cm). Pins are stored in these.</summary>
        public readonly List<Vector2> drawn = new List<Vector2>();
        /// <summary>
        /// Where each vertex is laid out before sewing (cm): the flat position with every pleat folded,
        /// and how many layers under the top it lies (<see cref="layoutDepth"/>, cm towards the body).
        /// </summary>
        public readonly List<Vector2> layout = new List<Vector2>();
        public readonly List<float> layoutDepth = new List<float>();
        public readonly List<int> piece = new List<int>();
        /// <summary>For boundary vertices, the edge index on its piece; -1 inside (and along dart legs, which are sewn).</summary>
        public readonly List<int> boundaryEdge = new List<int>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<Vector2> uv = new List<Vector2>();
        /// <summary>Vertex pairs sewn together.</summary>
        public readonly List<Vector2Int> stitches = new List<Vector2Int>();
        /// <summary>
        /// For each stitch, its group: a seam's index in the pattern's seam list, or (from the seam count
        /// on) one dart or one sewn pair of pleat folds. Stitches of a group are together, in order along it.
        /// </summary>
        public readonly List<int> stitchSeam = new List<int>();
        /// <summary>Mesh edges along pressed pleat folds: the cloth keeps its crease there.</summary>
        public readonly List<Vector2Int> creases = new List<Vector2Int>();
        public readonly List<string> warnings = new List<string>();
        /// <summary>What could not be kept as mesh edges, in words, when <see cref="warnings"/> says so.</summary>
        public readonly List<string> unrecovered = new List<string>();
        public int[] pieceStart;
        public int[] pieceCount;
        public PieceScale[] scales;
        public float edgeLengthCm;
        public string revision;

        public int VertexCount => flat.Count;
        public int TriangleCount => triangles.Count / 3;
    }

    /// <summary>
    /// Turns a pattern into a cloth mesh. Every edge is sampled along its length; the two sides of a seam
    /// get the same number of samples, so each stitch joins one vertex to one vertex whatever the curves
    /// and the mesh level. Darts are cut in (an edge dart's two legs, a double dart's hole) and sewn
    /// shut; a seam on an edge with darts or pleats skips the fabric they take out. Pleat folds run into
    /// the piece as lines of the mesh. The inside is filled with an even triangle lattice and
    /// triangulated (Delaunay) so outlines and fold lines are kept as edges.
    /// </summary>
    public static class PatternMesher
    {
        private const float Eps = 2e-5f;

        public static PatternMesh Build(ClothesPattern pattern, PieceScale[] scales, float edgeLengthCm)
        {
            float h = Mathf.Max(0.3f, edgeLengthCm);
            PatternMesh mesh = null;
            float boundaryFactor = 1f;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                mesh = TryBuild(pattern, scales, h, boundaryFactor, out bool recovered);
                if (recovered) break;
                boundaryFactor *= 0.8f;
                if (attempt == 4)
                {
                    string where = mesh.unrecovered.Count > 0 ? " (" + string.Join(", ", mesh.unrecovered) + ")" : "";
                    mesh.warnings.Add("Some outlines could not be kept as mesh edges" + where + "; they may be too narrow for this mesh level.");
                }
            }
            mesh.edgeLengthCm = h;
            mesh.scales = scales;
            mesh.revision = pattern.ComputeRevision() + "/" + h.ToString("0.###");
            UvLayout.ApplyUVs(pattern, mesh);
            return mesh;
        }

        private sealed class SeamPlan
        {
            public int seam, pa, ea, pb, eb;
            public List<Vector2> a, b;
            public float endA, endB;
        }

        private sealed class EdgeItem
        {
            public float a, b;
            public int count;
            public int dart = -1; // index into the piece's edge darts: the opening, replaced by the legs
        }

        private static PatternMesh TryBuild(ClothesPattern pattern, PieceScale[] scales, float h, float boundaryFactor, out bool recovered)
        {
            recovered = true;
            var mesh = new PatternMesh();
            int pieceTotal = pattern.pieces.Count;
            mesh.pieceStart = new int[pieceTotal];
            mesh.pieceCount = new int[pieceTotal];
            float bh = h * boundaryFactor;

            // Curves, darts, pleats and what they take out of each edge.
            var curves = new PatternGeometry.EdgeCurve[pieceTotal][];
            var intakes = new List<Vector2>[pieceTotal][];
            var edgeDarts = new List<DartGeometry.EdgeDartShape>[pieceTotal];
            var pleats = new List<(PatternPleat pleat, DartGeometry.PleatShape shape)>[pieceTotal];
            for (int p = 0; p < pieceTotal; p++)
            {
                PatternPiece piece = pattern.pieces[p];
                curves[p] = new PatternGeometry.EdgeCurve[piece.points.Count];
                intakes[p] = new List<Vector2>[piece.points.Count];
                for (int e = 0; e < piece.points.Count; e++)
                {
                    curves[p][e] = new PatternGeometry.EdgeCurve(piece, e, scales[p]);
                    intakes[p][e] = DartGeometry.Intakes(piece, e);
                }
                edgeDarts[p] = new List<DartGeometry.EdgeDartShape>();
                foreach (PatternDart dart in piece.darts)
                {
                    if (DartGeometry.EdgeDart(piece, dart, scales[p], out DartGeometry.EdgeDartShape s)) edgeDarts[p].Add(s);
                }
                pleats[p] = new List<(PatternPleat, DartGeometry.PleatShape)>();
                foreach (PatternPleat pleat in piece.pleats)
                {
                    if (DartGeometry.Pleat(piece, pleat, scales[p], out DartGeometry.PleatShape s)) pleats[p].Add((pleat, s));
                }
            }

            // Seams: both sides get the same number of samples over each matching stretch. A stretch
            // boundary is wherever either side has an intake, so what is left on one side meets what is
            // left on the other.
            var fixedCounts = new List<(int p, int e, float a, float b, int count)>();
            var plans = new List<SeamPlan>();
            for (int si = 0; si < pattern.seams.Count; si++)
            {
                PatternSeam seam = pattern.seams[si];
                if (seam == null) continue;
                if (!Resolve(pattern, seam.a, out int pa, out int ea) || !Resolve(pattern, seam.b, out int pb, out int eb)) continue;
                List<Vector2> a = Effective(intakes[pa][ea], seam.a.start, seam.a.end);
                List<Vector2> b = Effective(intakes[pb][eb], seam.b.start, seam.b.end);
                if (a.Count == 0 || b.Count == 0) continue;
                float la = curves[pa][ea].Length, lb = curves[pb][eb].Length;
                var breaks = new List<float> { 0f, 1f };
                breaks.AddRange(Boundaries(a));
                foreach (float u in Boundaries(b)) breaks.Add(seam.reversed ? 1f - u : u);
                breaks.Sort();
                for (int k = 0; k + 1 < breaks.Count; k++)
                {
                    float u0 = breaks[k], u1 = breaks[k + 1];
                    if (u1 - u0 < 1e-4f) continue;
                    float fa0 = MapU(a, u0, true), fa1 = MapU(a, u1, false);
                    float fb0, fb1;
                    if (seam.reversed) { fb0 = MapU(b, 1f - u1, true); fb1 = MapU(b, 1f - u0, false); }
                    else { fb0 = MapU(b, u0, true); fb1 = MapU(b, u1, false); }
                    int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max((fa1 - fa0) * la, (fb1 - fb0) * lb) / bh));
                    fixedCounts.Add((pa, ea, fa0, fa1, count));
                    fixedCounts.Add((pb, eb, fb0, fb1, count));
                }
                plans.Add(new SeamPlan { seam = si, pa = pa, ea = ea, pb = pb, eb = eb, a = a, b = b, endA = seam.a.end, endB = seam.b.end });
            }

            var edgeSamplesAll = new List<(float f, int v)>[pieceTotal][];
            int group = pattern.seams.Count;

            for (int p = 0; p < pieceTotal; p++)
            {
                PatternPiece piece = pattern.pieces[p];
                int n = piece.points.Count;
                mesh.pieceStart[p] = mesh.VertexCount;

                // How each edge is sampled: intervals between every break, an edge dart's opening replaced by its legs.
                var items = new List<EdgeItem>[n];
                for (int e = 0; e < n; e++)
                {
                    var breaks = new List<float> { 0f, 1f };
                    foreach (var f in fixedCounts) if (f.p == p && f.e == e) { breaks.Add(f.a); breaks.Add(f.b); }
                    foreach (Vector2 i in intakes[p][e]) { breaks.Add(i.x); breaks.Add(i.y); }
                    foreach (var pl in pleats[p]) if (pl.shape.edge == e) breaks.AddRange(pl.shape.lineFractions);
                    breaks.Sort();
                    items[e] = new List<EdgeItem>();
                    for (int i = 0; i + 1 < breaks.Count; i++)
                    {
                        float a = breaks[i], b = breaks[i + 1];
                        if (b - a < Eps) continue;
                        var item = new EdgeItem { a = a, b = b };
                        for (int d = 0; d < edgeDarts[p].Count; d++)
                        {
                            DartGeometry.EdgeDartShape s = edgeDarts[p][d];
                            if (s.edge == e && Mathf.Abs(s.f0 - a) < Eps && Mathf.Abs(s.f1 - b) < Eps) item.dart = d;
                        }
                        if (item.dart < 0)
                        {
                            item.count = -1;
                            foreach (var f in fixedCounts) if (f.p == p && f.e == e && Mathf.Abs(f.a - a) < Eps && Mathf.Abs(f.b - b) < Eps) { item.count = f.count; break; }
                            if (item.count < 0) item.count = Mathf.Max(1, Mathf.CeilToInt(curves[p][e].Length * (b - a) / bh));
                        }
                        items[e].Add(item);
                    }
                }

                // The outline, with dart legs.
                var points = new List<Vector2>();
                var pointEdge = new List<int>();
                var edgeSamples = new List<(float f, int v)>[n];
                var legs = new List<(int dart, List<int> legA, List<int> legB)>();
                var pointWhat = new List<string>();
                for (int e = 0; e < n; e++)
                {
                    edgeSamples[e] = new List<(float, int)>();
                    foreach (EdgeItem item in items[e])
                    {
                        if (item.dart >= 0)
                        {
                            DartGeometry.EdgeDartShape s = edgeDarts[p][item.dart];
                            // Legs that bow close together are sampled finer, or the mesh would join one leg to the other.
                            float gap = (s.LegA(0.5f) - s.LegB(0.5f)).magnitude;
                            float spacing = Mathf.Min(bh, Mathf.Max(0.25f, gap * 1.2f));
                            int count = Mathf.Max(1, Mathf.CeilToInt(s.LegLength / spacing));
                            var legA = new List<int>();
                            var legB = new List<int>();
                            string what = "a leg of a dart on '" + piece.name + "'";
                            for (int k = 0; k < count; k++) { legA.Add(points.Count); points.Add(s.LegA(k / (float)count)); pointEdge.Add(k == 0 ? e : -1); pointWhat.Add(what); }
                            for (int k = 0; k < count; k++) { legB.Add(points.Count); points.Add(s.LegB(k / (float)count)); pointEdge.Add(-1); pointWhat.Add(what); }
                            legs.Add((item.dart, legA, legB));
                            continue;
                        }
                        for (int k = 0; k < item.count; k++)
                        {
                            float f = item.a + (item.b - item.a) * k / item.count;
                            edgeSamples[e].Add((f, points.Count));
                            points.Add(curves[p][e].At(f));
                            pointEdge.Add(e);
                            pointWhat.Add("the outline of '" + piece.name + "'");
                        }
                    }
                }
                int outlineCount = points.Count;
                if (outlineCount < 3) { mesh.warnings.Add("'" + piece.name + "' is too small to mesh."); mesh.pieceCount[p] = 0; edgeSamplesAll[p] = edgeSamples; continue; }
                var outline = points.GetRange(0, outlineCount);
                bool clockwise = PatternGeometry.SignedArea(outline) < 0f;
                int OutlineVertexAt(int e, float f)
                {
                    if (f >= 1f - Eps) { e = (e + 1) % n; f = 0f; }
                    foreach (var s in edgeSamples[e]) if (Mathf.Abs(s.f - f) < Eps) return s.v;
                    return -1;
                }

                // Double-dart holes.
                var holes = new List<(int start, int perSide)>();
                var holePolygons = new List<List<Vector2>>();
                foreach (PatternDart dart in piece.darts)
                {
                    if (dart.kind != DartKind.Double) continue;
                    List<Vector2> hole = DartGeometry.DoubleOutline(dart, bh, scales[p], out int perSide);
                    holes.Add((points.Count, perSide));
                    holePolygons.Add(hole);
                    foreach (Vector2 q in hole) { points.Add(q); pointEdge.Add(-1); }
                }

                // Pleat fold lines, from their point on the edge into the piece.
                var pleatLines = new List<List<int>[]>();
                var linePolylines = new List<List<Vector2>>();
                foreach (var pl in pleats[p])
                {
                    var lines = new List<int>[pl.shape.lineFractions.Length];
                    for (int i = 0; i < lines.Length; i++)
                    {
                        int start = OutlineVertexAt(pl.shape.edge, pl.shape.lineFractions[i]);
                        lines[i] = new List<int>();
                        if (start < 0) { mesh.warnings.Add("A pleat on '" + piece.name + "' could not be placed on its edge."); continue; }
                        lines[i].Add(start);
                        var polyline = new List<Vector2> { points[start] };
                        foreach (Vector2 q in DartGeometry.PleatLine(pl.pleat, pl.shape, points[start], outline, bh))
                        {
                            lines[i].Add(points.Count);
                            points.Add(q);
                            pointEdge.Add(-1);
                            polyline.Add(q);
                        }
                        linePolylines.Add(polyline);
                    }
                    if (pl.pleat.depthCm < bh * 0.6f) mesh.warnings.Add("A pleat on '" + piece.name + "' is shallower than this mesh level can show; use a finer Mesh Level.");
                    pleatLines.Add(lines);
                }
                int constrainedCount = points.Count;

                // Interior lattice, kept away from every outline, hole and fold line so they stay mesh edges.
                Rect bounds = PolygonBounds(outline);
                float keepAway = 0.65f * h;
                var near = new SegmentIndex(bounds, h, keepAway);
                near.AddPolyline(outline, true);
                foreach (List<Vector2> hole in holePolygons) near.AddPolyline(hole, true);
                foreach (List<Vector2> line in linePolylines) near.AddPolyline(line, false);
                float rowStep = h * 0.8660254f;
                int row = 0;
                for (float y = bounds.yMin + rowStep * 0.5f; y < bounds.yMax; y += rowStep, row++)
                {
                    float offset = (row & 1) == 0 ? 0f : h * 0.5f;
                    for (float x = bounds.xMin + offset; x < bounds.xMax; x += h)
                    {
                        var q = new Vector2(x, y);
                        if (near.Near(q)) continue;
                        if (!PatternGeometry.PointInPolygon(outline, q)) continue;
                        bool inHole = false;
                        foreach (List<Vector2> hole in holePolygons) if (PatternGeometry.PointInPolygon(hole, q)) { inHole = true; break; }
                        if (inHole) continue;
                        points.Add(q);
                        pointEdge.Add(-1);
                    }
                }

                var delaunay = new Delaunay2D(bounds);
                foreach (Vector2 q in points) delaunay.Insert(q);
                List<int> tris = delaunay.Triangles();

                int baseIndex = mesh.VertexCount;
                for (int i = 0; i < points.Count; i++) AddVertex(mesh, scales[p], points[i], p, pointEdge[i]);

                var edgeSet = new HashSet<long>(EdgeKeyComparer.Instance);
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    Vector2 pa = points[a], pb = points[b], pc = points[c];
                    Vector2 centroid = (pa + pb + pc) / 3f;
                    if (!PatternGeometry.PointInPolygon(outline, centroid)) continue;
                    bool inHole = false;
                    foreach (List<Vector2> hole in holePolygons) if (PatternGeometry.PointInPolygon(hole, centroid)) { inHole = true; break; }
                    if (inHole) continue;
                    if (Mathf.Abs((pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x)) < 1e-6f) continue;
                    if (clockwise) { int tmp = b; b = c; c = tmp; }
                    mesh.triangles.Add(baseIndex + a); mesh.triangles.Add(baseIndex + b); mesh.triangles.Add(baseIndex + c);
                    edgeSet.Add(EdgeKey(a, b)); edgeSet.Add(EdgeKey(b, c)); edgeSet.Add(EdgeKey(c, a));
                }
                // Every outline, hole and fold segment must be a mesh edge, or the stitches and creases would not hold.
                bool lost = false;
                void Unrecovered(string what) { lost = true; if (!mesh.unrecovered.Contains(what)) mesh.unrecovered.Add(what); }
                for (int i = 0; i < outlineCount; i++) if (!edgeSet.Contains(EdgeKey(i, (i + 1) % outlineCount))) Unrecovered(pointWhat[i]);
                foreach (var hole in holes)
                {
                    int count = hole.perSide * 2;
                    for (int i = 0; i < count; i++) if (!edgeSet.Contains(EdgeKey(hole.start + i, hole.start + (i + 1) % count))) Unrecovered("a diamond dart on '" + piece.name + "'");
                }
                foreach (List<int>[] lines in pleatLines)
                {
                    foreach (List<int> line in lines)
                    {
                        for (int i = 0; i + 1 < line.Count; i++) if (!edgeSet.Contains(EdgeKey(line[i], line[i + 1]))) Unrecovered("a pleat fold on '" + piece.name + "'");
                    }
                }
                if (lost) recovered = false;
                mesh.pieceCount[p] = mesh.VertexCount - mesh.pieceStart[p];

                // Edge darts: each leg sewn to the other, from the opening to the point.
                foreach (var leg in legs)
                {
                    DartGeometry.EdgeDartShape s = edgeDarts[p][leg.dart];
                    int count = leg.legA.Count;
                    int mouthEnd = OutlineVertexAt(s.edge, s.f1);
                    if (mouthEnd >= 0) AddStitch(mesh, baseIndex + leg.legA[0], baseIndex + mouthEnd, group);
                    for (int k = 1; k < count; k++) AddStitch(mesh, baseIndex + leg.legA[k], baseIndex + leg.legB[count - k], group);
                    group++;
                }
                // Double darts: one side sewn to the other, point to point.
                foreach (var hole in holes)
                {
                    for (int k = 1; k < hole.perSide; k++) AddStitch(mesh, baseIndex + hole.start + k, baseIndex + hole.start + hole.perSide + (hole.perSide - k), group);
                    group++;
                }
                // Pleats: the folds sewn together at the edge and down the stitched length; pressed folds keep their crease.
                for (int i = 0; i < pleats[p].Count; i++)
                {
                    PatternPleat pleat = pleats[p][i].pleat;
                    DartGeometry.PleatShape s = pleats[p][i].shape;
                    List<int>[] lines = pleatLines[i];
                    int down = pleat.stitchedCm > 0f ? Mathf.RoundToInt(pleat.stitchedCm / bh) : 0;
                    foreach (Vector2Int pair in s.stitchedLines)
                    {
                        List<int> la = lines[pair.x], lb = lines[pair.y];
                        for (int k = 0; k <= down && k < la.Count && k < lb.Count; k++) AddStitch(mesh, baseIndex + la[k], baseIndex + lb[k], group);
                        group++;
                    }
                    if (!pleat.pressed) continue;
                    for (int l = 0; l < lines.Length; l++)
                    {
                        if (!s.crease[l]) continue;
                        for (int k = 0; k + 1 < lines[l].Count; k++) mesh.creases.Add(new Vector2Int(baseIndex + lines[l][k], baseIndex + lines[l][k + 1]));
                    }
                }

                var global = new List<(float f, int v)>[n];
                for (int e = 0; e < n; e++)
                {
                    global[e] = new List<(float, int)>(edgeSamples[e].Count);
                    foreach (var s in edgeSamples[e]) global[e].Add((s.f, baseIndex + s.v));
                }
                edgeSamplesAll[p] = global;

                LayOutPleats(mesh, p, pleats[p], curves[p]);
            }

            // Seam stitches: what is left on each side, in order, one vertex to one vertex.
            foreach (SeamPlan plan in plans)
            {
                List<int> va = SpanVertices(pattern, edgeSamplesAll, plan.pa, plan.ea, plan.a, plan.endA);
                List<int> vb = SpanVertices(pattern, edgeSamplesAll, plan.pb, plan.eb, plan.b, plan.endB);
                if (va.Count != vb.Count || va.Count == 0)
                {
                    mesh.warnings.Add("A seam's two sides could not be matched.");
                    continue;
                }
                bool reversed = pattern.seams[plan.seam].reversed;
                for (int k = 0; k < va.Count; k++)
                {
                    int other = reversed ? vb[vb.Count - 1 - k] : vb[k];
                    if (va[k] == other) continue;
                    AddStitch(mesh, va[k], other, plan.seam);
                }
            }
            return mesh;
        }

        private static void AddStitch(PatternMesh mesh, int a, int b, int group)
        {
            if (a == b) return;
            mesh.stitches.Add(new Vector2Int(a, b));
            mesh.stitchSeam.Add(group);
        }

        /// <summary>
        /// Folds each pleat in the layout: the fabric between its fold lines is laid back on itself, a layer
        /// under the one above, and everything past the pleat moves up by what it took out.
        /// </summary>
        private static void LayOutPleats(PatternMesh mesh, int p, List<(PatternPleat pleat, DartGeometry.PleatShape shape)> pleats, PatternGeometry.EdgeCurve[] curves)
        {
            int start = mesh.pieceStart[p], end = start + mesh.pieceCount[p];
            for (int v = start; v < end; v++)
            {
                Vector2 position = mesh.flat[v];
                Vector2 shift = Vector2.zero;
                int depth = 0;
                foreach (var pl in pleats)
                {
                    DartGeometry.PleatShape s = pl.shape;
                    Vector2 origin = curves[s.edge].At(s.lineFractions[0]);
                    int lines = s.lineFractions.Length;
                    var u = new float[lines];
                    var mapped = new float[lines];
                    for (int i = 0; i < lines; i++) u[i] = Vector2.Dot(curves[s.edge].At(s.lineFractions[i]) - origin, s.tangent);
                    for (int i = 0; i + 1 < lines; i++) mapped[i + 1] = mapped[i] + s.direction[i] * (u[i + 1] - u[i]);
                    float at = Vector2.Dot(position - origin, s.tangent);
                    if (at <= u[0]) continue;
                    float moved;
                    if (at >= u[lines - 1]) moved = mapped[lines - 1] - u[lines - 1];
                    else
                    {
                        int i = 0;
                        while (i + 1 < lines - 1 && at > u[i + 1]) i++;
                        moved = mapped[i] + s.direction[i] * (at - u[i]) - at;
                        depth = Mathf.Max(depth, s.layer[i]);
                    }
                    shift += s.tangent * moved;
                }
                mesh.layout[v] = position + shift;
                mesh.layoutDepth[v] = depth * DartGeometry.PleatLayerCm;
            }
        }

        private static void AddVertex(PatternMesh mesh, PieceScale scale, Vector2 evaluated, int piece, int edge)
        {
            mesh.flat.Add(evaluated);
            mesh.drawn.Add(scale.Invert(evaluated));
            mesh.layout.Add(evaluated);
            mesh.layoutDepth.Add(0f);
            mesh.piece.Add(piece);
            mesh.boundaryEdge.Add(edge);
            mesh.uv.Add(Vector2.zero);
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static Rect PolygonBounds(List<Vector2> polygon)
        {
            Vector2 min = polygon[0], max = polygon[0];
            foreach (Vector2 p in polygon) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// Line segments binned in a grid, each in every cell it passes within the keep-away distance of,
        /// so "is any segment that close" only looks at one cell's segments.
        /// </summary>
        private sealed class SegmentIndex
        {
            private readonly Rect bounds;
            private readonly float cell, radius;
            private readonly int columns, rows;
            private readonly List<(Vector2 a, Vector2 b)>[] cells;

            public SegmentIndex(Rect bounds, float cellSize, float radius)
            {
                this.bounds = bounds;
                cell = Mathf.Max(0.05f, cellSize);
                this.radius = radius;
                columns = Mathf.Max(1, Mathf.CeilToInt(bounds.width / cell) + 1);
                rows = Mathf.Max(1, Mathf.CeilToInt(bounds.height / cell) + 1);
                cells = new List<(Vector2, Vector2)>[columns * rows];
            }

            public void AddPolyline(List<Vector2> points, bool closed)
            {
                int count = closed ? points.Count : points.Count - 1;
                for (int i = 0; i < count; i++) Add(points[i], points[(i + 1) % points.Count]);
                if (points.Count == 1) Add(points[0], points[0]);
            }

            private void Add(Vector2 a, Vector2 b)
            {
                Vector2 min = Vector2.Min(a, b) - Vector2.one * radius, max = Vector2.Max(a, b) + Vector2.one * radius;
                int x0 = Column(min.x), x1 = Column(max.x), y0 = Row(min.y), y1 = Row(max.y);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * columns + x;
                        (cells[i] ?? (cells[i] = new List<(Vector2, Vector2)>())).Add((a, b));
                    }
                }
            }

            private int Column(float x) => Mathf.Clamp(Mathf.FloorToInt((x - bounds.xMin) / cell), 0, columns - 1);
            private int Row(float y) => Mathf.Clamp(Mathf.FloorToInt((y - bounds.yMin) / cell), 0, rows - 1);

            public bool Near(Vector2 p)
            {
                List<(Vector2 a, Vector2 b)> list = cells[Row(p.y) * columns + Column(p.x)];
                if (list == null) return false;
                float r2 = radius * radius;
                foreach (var s in list)
                {
                    Vector2 ab = s.b - s.a;
                    float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / ab.sqrMagnitude) : 0f;
                    if ((s.a + ab * t - p).sqrMagnitude < r2) return true;
                }
                return false;
            }
        }

        private static bool Resolve(ClothesPattern pattern, SeamSpan span, out int pieceIndex, out int edgeIndex)
        {
            pieceIndex = pattern.IndexOfPiece(span.pieceId);
            edgeIndex = pieceIndex >= 0 ? pattern.pieces[pieceIndex].IndexOfEdge(span.edgeId) : -1;
            return edgeIndex >= 0 && span.end > span.start;
        }

        /// <summary>The parts of a span left after the intakes (fractions, in order).</summary>
        private static List<Vector2> Effective(List<Vector2> intakes, float start, float end)
        {
            var parts = new List<Vector2>();
            float at = start;
            foreach (Vector2 i in intakes)
            {
                if (i.y <= start || i.x >= end) continue;
                if (i.x > at) parts.Add(new Vector2(at, Mathf.Min(i.x, end)));
                at = Mathf.Max(at, i.y);
            }
            if (end > at) parts.Add(new Vector2(at, end));
            return parts;
        }

        private static float Total(List<Vector2> parts)
        {
            float total = 0f;
            foreach (Vector2 part in parts) total += part.y - part.x;
            return total;
        }

        /// <summary>Where one part ends and the next begins, as fractions of the length left.</summary>
        private static IEnumerable<float> Boundaries(List<Vector2> parts)
        {
            float total = Total(parts), at = 0f;
            for (int i = 0; i + 1 < parts.Count; i++)
            {
                at += parts[i].y - parts[i].x;
                yield return at / total;
            }
        }

        /// <summary>The edge fraction at a fraction of the length left. At an intake, the start of the next part (or the end of the last).</summary>
        private static float MapU(List<Vector2> parts, float u, bool atStart)
        {
            float total = Total(parts), target = u * total, at = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                float length = parts[i].y - parts[i].x;
                bool inside = atStart ? target < at + length - 1e-6f : target <= at + length + 1e-6f;
                if (inside) return parts[i].x + Mathf.Clamp(target - at, 0f, length);
                at += length;
            }
            return parts[parts.Count - 1].y;
        }

        /// <summary>The vertices along what is left of a span, in order, ending with its end point.</summary>
        private static List<int> SpanVertices(ClothesPattern pattern, List<(float f, int v)>[][] samples, int p, int e, List<Vector2> parts, float end)
        {
            var result = new List<int>();
            if (samples[p] == null) return result;
            foreach (Vector2 part in parts)
            {
                foreach (var s in samples[p][e]) if (s.f >= part.x - Eps && s.f < part.y - Eps) result.Add(s.v);
            }
            int edge = e;
            float f = end;
            if (f >= 1f - Eps) { edge = (e + 1) % pattern.pieces[p].points.Count; f = 0f; }
            foreach (var s in samples[p][edge]) if (Mathf.Abs(s.f - f) < Eps) { result.Add(s.v); break; }
            return result;
        }
    }
}
