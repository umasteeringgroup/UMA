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
        /// <summary>Each piece's own edge length (cm): the Mesh Level's, divided by the piece's mesh detail.</summary>
        public float[] pieceEdgeCm;
        /// <summary>Indices into <see cref="stitches"/> of the stitches on folded seams.</summary>
        public readonly List<int> foldedStitches = new List<int>();
        /// <summary>
        /// For each entry of <see cref="creases"/>: how far the cloth is folded there from flat, in degrees
        /// (a fold line drawn inside a piece), or -1 for a pleat's fold, which rests folded as laid out.
        /// </summary>
        public readonly List<float> creaseAngle = new List<float>();
        /// <summary>Per crease: baked sharp (a normal for each side; see <see cref="PatternLine.sharp"/> and <see cref="PatternPleat.sharpFolds"/>).</summary>
        public readonly List<bool> creaseSharp = new List<bool>();
        /// <summary>Per vertex: on the rim of a hole cut in a piece. A hole's rim is an opening.</summary>
        public readonly List<bool> onHole = new List<bool>();
        /// <summary>Indices into <see cref="stitches"/> of the single stitches (tacks and buttons).</summary>
        public readonly List<int> tackStitches = new List<int>();
        /// <summary>For each of the pattern's tacks: its vertex on the first piece and on the second (-1 when it could not be placed).</summary>
        public readonly List<Vector2Int> tackVertices = new List<Vector2Int>();
        /// <summary>Mesh edges that run along a piece's outline: the two vertices and the edge's index on the piece.</summary>
        public readonly List<Vector3Int> outlineSegments = new List<Vector3Int>();
        /// <summary>
        /// For a mesh made of quads (<see cref="QuadRemesher"/>): four vertices per quad, counter-clockwise as
        /// drawn. Each quad is also two of the first triangles, in order; any triangles left over follow them.
        /// </summary>
        public readonly List<int> quads = new List<int>();
        public int QuadCount => quads.Count / 4;

        /// <summary>A piece's edge length (cm).</summary>
        public float EdgeCmOf(int piece) => pieceEdgeCm != null && piece >= 0 && piece < pieceEdgeCm.Length ? pieceEdgeCm[piece] : edgeLengthCm;
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

        public static PatternMesh Build(ClothesPattern pattern, PieceScale[] scales, float edgeLengthCm) =>
            Build(pattern, scales, edgeLengthCm, pattern.meshTopology, false);

        /// <param name="topology">How the inside of each piece is filled (the pattern's own setting is not read).</param>
        /// <param name="outlineLoop">
        /// Also a row of points one spacing in from every outline, so the cloth next to an outline is a strip of
        /// near-squares: an edge loop following the hem or seam (<see cref="QuadRemesher"/>).
        /// </param>
        /// <param name="pleatSpacingScale">
        /// How much coarser than usual pleated pieces may be meshed: 2 for a mesh that will be split in two again
        /// (<see cref="QuadRemesher"/>'s all quads), which would otherwise come out twice as fine as its pleats need.
        /// </param>
        public static PatternMesh Build(ClothesPattern pattern, PieceScale[] scales, float edgeLengthCm, MeshTopology topology, bool outlineLoop, float pleatSpacingScale = 1f)
        {
            float h = Mathf.Max(0.3f, edgeLengthCm);
            PatternMesh mesh = null;
            float boundaryFactor = 1f;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                mesh = TryBuild(pattern, scales, h, boundaryFactor, topology, outlineLoop, pleatSpacingScale, out bool recovered);
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

        private static PatternMesh TryBuild(ClothesPattern pattern, PieceScale[] scales, float hBase, float boundaryFactor, MeshTopology topology, bool outlineLoop, float pleatSpacingScale, out bool recovered)
        {
            recovered = true;
            var mesh = new PatternMesh();
            int pieceTotal = pattern.pieces.Count;
            mesh.pieceStart = new int[pieceTotal];
            mesh.pieceCount = new int[pieceTotal];
            // Each piece's spacing: the Mesh Level's, finer for pieces with more detail, and fine enough for its
            // pleats (see PleatEdgeCm).
            var pieceH = new float[pieceTotal];
            for (int p = 0; p < pieceTotal; p++) pieceH[p] = Mathf.Min(hBase / PatternPiece.DetailOf(pattern.pieces[p]), PleatEdgeCm(pattern.pieces[p]) * Mathf.Max(1f, pleatSpacingScale));
            mesh.pieceEdgeCm = (float[])pieceH.Clone();

            // Curves, darts, pleats and what they take out of each edge.
            // Lines drawn inside a piece are sampled like edges: line l of a piece is its "edge" n + l.
            var curves = new PatternGeometry.EdgeCurve[pieceTotal][];
            var intakes = new List<Vector2>[pieceTotal][];
            var edgeDarts = new List<DartGeometry.EdgeDartShape>[pieceTotal];
            var pleats = new List<(PatternPleat pleat, DartGeometry.PleatShape shape)>[pieceTotal];
            // Points every edge and line must have a vertex at: a line's corners, and where a line's end meets an edge.
            var extraBreaks = new List<float>[pieceTotal][];
            // For each line: the edge and fraction its start and its end sit on, or edge -1 for an end inside the piece.
            var lineEnds = new (int startEdge, float startF, int endEdge, float endF)[pieceTotal][];
            for (int p = 0; p < pieceTotal; p++)
            {
                PatternPiece piece = pattern.pieces[p];
                int edgeCount = piece.points.Count, lineCount = piece.lines != null ? piece.lines.Count : 0;
                curves[p] = new PatternGeometry.EdgeCurve[edgeCount + lineCount];
                intakes[p] = new List<Vector2>[edgeCount + lineCount];
                extraBreaks[p] = new List<float>[edgeCount + lineCount];
                lineEnds[p] = new (int, float, int, float)[lineCount];
                for (int e = 0; e < edgeCount + lineCount; e++) extraBreaks[p][e] = new List<float>();
                for (int e = 0; e < piece.points.Count; e++)
                {
                    curves[p][e] = new PatternGeometry.EdgeCurve(piece, e, scales[p]);
                    intakes[p][e] = DartGeometry.Intakes(piece, e);
                }
                for (int l = 0; l < lineCount; l++)
                {
                    PatternLine line = piece.lines[l];
                    intakes[p][edgeCount + l] = new List<Vector2>();
                    lineEnds[p][l] = (-1, 0f, -1, 0f);
                    if (line == null || line.points.Count < 2) continue;
                    PatternGeometry.EdgeCurve curve = LineGeometry.Curve(line, scales[p]);
                    curves[p][edgeCount + l] = curve;
                    bool closed = LineGeometry.IsClosed(line);
                    for (int k = 1; k < line.points.Count - (closed ? 0 : 1); k++) extraBreaks[p][edgeCount + l].Add(curve.FractionOfPoint(k));
                    if (closed) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        Vector2 at = line.points[end == 0 ? 0 : line.points.Count - 1];
                        if (!LineGeometry.OnOutline(piece, at, LineGeometry.SnapCm, out int onEdge, out float fraction)) continue;
                        // At a corner the outline already has a vertex; elsewhere the edge gets one.
                        if (fraction > 1f - 0.01f) { onEdge = (onEdge + 1) % edgeCount; fraction = 0f; }
                        else if (fraction < 0.01f) fraction = 0f;
                        bool inIntake = false;
                        foreach (Vector2 intake in intakes[p][onEdge]) if (fraction > intake.x - Eps && fraction < intake.y + Eps) inIntake = true;
                        if (inIntake) { mesh.warnings.Add("A line on '" + piece.name + "' ends in a dart or pleat; move its end along the edge."); continue; }
                        if (fraction > 0f) extraBreaks[p][onEdge].Add(fraction);
                        if (end == 0) { lineEnds[p][l].startEdge = onEdge; lineEnds[p][l].startF = fraction; }
                        else { lineEnds[p][l].endEdge = onEdge; lineEnds[p][l].endF = fraction; }
                    }
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
                // A vertex one side must have (a line's corner, a line ending on the edge) is a vertex on both.
                foreach (float extra in extraBreaks[pa][ea]) if (Unmap(a, extra, out float u)) breaks.Add(u);
                foreach (float extra in extraBreaks[pb][eb]) if (Unmap(b, extra, out float u)) breaks.Add(seam.reversed ? 1f - u : u);
                breaks.Sort();
                for (int k = 0; k + 1 < breaks.Count; k++)
                {
                    float u0 = breaks[k], u1 = breaks[k + 1];
                    if (u1 - u0 < 1e-4f) continue;
                    float fa0 = MapU(a, u0, true), fa1 = MapU(a, u1, false);
                    float fb0, fb1;
                    if (seam.reversed) { fb0 = MapU(b, 1f - u1, true); fb1 = MapU(b, 1f - u0, false); }
                    else { fb0 = MapU(b, u0, true); fb1 = MapU(b, u1, false); }
                    // Each side asks for as many points as its own spacing needs; the finer side wins.
                    int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max((fa1 - fa0) * la / (pieceH[pa] * boundaryFactor), (fb1 - fb0) * lb / (pieceH[pb] * boundaryFactor))));
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
                float h = pieceH[p], bh = h * boundaryFactor;
                int n = piece.points.Count;
                int lineTotal = piece.lines != null ? piece.lines.Count : 0;
                mesh.pieceStart[p] = mesh.VertexCount;

                // How each edge (and line) is sampled: intervals between every break, an edge dart's opening replaced by its legs.
                var items = new List<EdgeItem>[n + lineTotal];
                for (int e = 0; e < n + lineTotal; e++)
                {
                    items[e] = new List<EdgeItem>();
                    if (curves[p][e] == null) continue;
                    var breaks = new List<float> { 0f, 1f };
                    foreach (var f in fixedCounts) if (f.p == p && f.e == e) { breaks.Add(f.a); breaks.Add(f.b); }
                    foreach (Vector2 i in intakes[p][e]) { breaks.Add(i.x); breaks.Add(i.y); }
                    foreach (var pl in pleats[p]) if (pl.shape.edge == e) breaks.AddRange(pl.shape.lineFractions);
                    breaks.AddRange(extraBreaks[p][e]);
                    breaks.Sort();
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
                var edgeSamples = new List<(float f, int v)>[n + lineTotal];
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

                // Lines drawn inside the piece: kept as mesh edges, so they can be sewn to, creased or cut out.
                var lineVertices = new List<int>[lineTotal];
                var holeRims = new List<int>();
                for (int l = 0; l < lineTotal; l++)
                {
                    int e = n + l;
                    PatternGeometry.EdgeCurve curve = curves[p][e];
                    if (curve == null) continue;
                    PatternLine line = piece.lines[l];
                    bool closed = LineGeometry.IsClosed(line);
                    int startVertex = lineEnds[p][l].startEdge >= 0 ? OutlineVertexAt(lineEnds[p][l].startEdge, lineEnds[p][l].startF) : -1;
                    int endVertex = lineEnds[p][l].endEdge >= 0 ? OutlineVertexAt(lineEnds[p][l].endEdge, lineEnds[p][l].endF) : -1;
                    edgeSamples[e] = new List<(float, int)>();
                    var vertices = new List<int>();
                    // Lines that meet (a pocket's sides at its corners) share the vertex there: two points
                    // in one place would break the triangulation.
                    int SharedOrNew(Vector2 q)
                    {
                        for (int i = outlineCount; i < points.Count; i++) if ((points[i] - q).sqrMagnitude < 1e-6f) return i;
                        points.Add(q); pointEdge.Add(-1);
                        return points.Count - 1;
                    }
                    foreach (EdgeItem item in items[e])
                    {
                        for (int k = 0; k < item.count; k++)
                        {
                            float f = item.a + (item.b - item.a) * k / item.count;
                            int v;
                            if (!closed && f < Eps && startVertex >= 0) v = startVertex;
                            else if (!closed && f < Eps) v = SharedOrNew(curve.At(f));
                            else { v = points.Count; points.Add(curve.At(f)); pointEdge.Add(-1); }
                            edgeSamples[e].Add((f, v));
                            vertices.Add(v);
                        }
                    }
                    if (vertices.Count == 0) continue;
                    int last;
                    if (closed) last = vertices[0];
                    else if (endVertex >= 0) last = endVertex;
                    else last = SharedOrNew(curve.At(1f));
                    edgeSamples[e].Add((1f, last));
                    vertices.Add(last);
                    lineVertices[l] = vertices;
                    var polyline = new List<Vector2>(vertices.Count);
                    foreach (int v in vertices) polyline.Add(points[v]);
                    linePolylines.Add(polyline);
                    if (line.kind == PatternLineKind.Hole)
                    {
                        holePolygons.Add(polyline.GetRange(0, polyline.Count - 1));
                        holeRims.AddRange(vertices);
                    }
                }

                // Tacks: a vertex where each one holds this piece, on a point that is already there when one is close.
                var tackPoints = new List<Vector2>();
                var tackLocal = new List<(int tack, bool first, int v)>();
                if (pattern.tacks != null)
                {
                    for (int ti = 0; ti < pattern.tacks.Count; ti++)
                    {
                        PatternTack tack = pattern.tacks[ti];
                        if (tack == null) continue;
                        for (int side = 0; side < 2; side++)
                        {
                            if ((side == 0 ? tack.pieceA : tack.pieceB) != piece.id) continue;
                            Vector2 q = scales[p].Apply(side == 0 ? tack.a : tack.b);
                            int v = -1;
                            float nearest = 0.4f * h;
                            for (int i = 0; i < points.Count; i++)
                            {
                                float d = (points[i] - q).magnitude;
                                if (d < nearest) { nearest = d; v = i; }
                            }
                            if (v < 0)
                            {
                                if (!PatternGeometry.PointInPolygon(outline, q)) { mesh.warnings.Add("A tack on '" + piece.name + "' is outside the piece."); continue; }
                                v = points.Count;
                                points.Add(q);
                                pointEdge.Add(-1);
                                tackPoints.Add(q);
                            }
                            tackLocal.Add((ti, side == 0, v));
                        }
                    }
                }
                int constrainedCount = points.Count;

                // Interior lattice, kept away from every outline, hole and fold line so they stay mesh edges.
                Rect bounds = PolygonBounds(outline);
                float keepAway = 0.65f * h;
                var near = new SegmentIndex(bounds, h, keepAway);
                near.AddPolyline(outline, true);
                foreach (List<Vector2> hole in holePolygons) near.AddPolyline(hole, true);
                foreach (List<Vector2> line in linePolylines) near.AddPolyline(line, false);
                foreach (Vector2 q in tackPoints) near.AddPolyline(new List<Vector2> { q }, false);
                bool TakeInterior(Vector2 q)
                {
                    if (near.Near(q)) return false;
                    if (!PatternGeometry.PointInPolygon(outline, q)) return false;
                    foreach (List<Vector2> hole in holePolygons) if (PatternGeometry.PointInPolygon(hole, q)) return false;
                    points.Add(q);
                    pointEdge.Add(-1);
                    return true;
                }
                if (outlineLoop)
                {
                    // A loop one spacing in from the outline, a point opposite each outline sample: the strip between
                    // is near-squares, an edge loop along every hem and seam. Points that would come too close to
                    // anything (a sharp corner, a dart, a narrow part) are left out.
                    var loop = new List<Vector2>();
                    for (int i = 0; i < outlineCount; i++)
                    {
                        Vector2 prev = outline[(i + outlineCount - 1) % outlineCount], here = outline[i], next = outline[(i + 1) % outlineCount];
                        Vector2 dPrev = (here - prev).normalized, dNext = (next - here).normalized;
                        // Inward is left of the direction for a counter-clockwise outline.
                        Vector2 nPrev = new Vector2(-dPrev.y, dPrev.x), nNext = new Vector2(-dNext.y, dNext.x);
                        if (clockwise) { nPrev = -nPrev; nNext = -nNext; }
                        Vector2 inward = (nPrev + nNext).normalized;
                        if (inward.sqrMagnitude < 0.5f) continue;
                        float depth = 0.9f * h / Mathf.Max(0.5f, Vector2.Dot(inward, nPrev));
                        Vector2 q = here + inward * depth;
                        if (!PatternGeometry.PointInPolygon(outline, q) || PatternGeometry.DistanceToPolyline(outline, q) < 0.6f * h) continue;
                        bool clear = true;
                        foreach (List<Vector2> hole in holePolygons) if (PatternGeometry.PointInPolygon(hole, q) || PatternGeometry.DistanceToPolyline(hole, q) < 0.6f * h) { clear = false; break; }
                        foreach (List<Vector2> line in linePolylines) if (clear && PatternGeometry.DistanceToPolyline(line, q) < 0.6f * h) clear = false;
                        foreach (Vector2 other in loop) if (clear && (other - q).sqrMagnitude < 0.36f * h * h) clear = false;
                        foreach (Vector2 t in tackPoints) if (clear && (t - q).sqrMagnitude < 0.36f * h * h) clear = false;
                        if (!clear) continue;
                        loop.Add(q);
                        points.Add(q);
                        pointEdge.Add(-1);
                    }
                    foreach (Vector2 q in loop) near.AddPolyline(new List<Vector2> { q }, false);
                }
                if (topology == MeshTopology.GrainGrid)
                {
                    // Squares along the grain and across it, split in two: triangles that pair into quads running
                    // with the warp and the weft.
                    float g = piece.grainAngle * Mathf.Deg2Rad;
                    Vector2 along = new Vector2(-Mathf.Sin(g), Mathf.Cos(g)), across = new Vector2(along.y, -along.x);
                    Vector2 middle = bounds.center;
                    float reach = (bounds.size.magnitude * 0.5f) / h + 1f;
                    int cells = Mathf.CeilToInt(reach);
                    for (int j = -cells; j <= cells; j++)
                    {
                        for (int i = -cells; i <= cells; i++)
                        {
                            Vector2 q = middle + across * ((i + 0.5f) * h) + along * ((j + 0.5f) * h);
                            if (bounds.Contains(q)) TakeInterior(q);
                        }
                    }
                }
                else
                {
                    float rowStep = h * 0.8660254f;
                    int row = 0;
                    for (float y = bounds.yMin + rowStep * 0.5f; y < bounds.yMax; y += rowStep, row++)
                    {
                        float offset = (row & 1) == 0 ? 0f : h * 0.5f;
                        for (float x = bounds.xMin + offset; x < bounds.xMax; x += h) TakeInterior(new Vector2(x, y));
                    }
                }

                var delaunay = new Delaunay2D(bounds);
                foreach (Vector2 q in points) delaunay.Insert(q);
                List<int> tris = delaunay.Triangles();

                int baseIndex = mesh.VertexCount;
                for (int i = 0; i < points.Count; i++) AddVertex(mesh, scales[p], points[i], p, pointEdge[i]);
                foreach (int v in holeRims) mesh.onHole[baseIndex + v] = true;
                foreach (var t in tackLocal)
                {
                    while (mesh.tackVertices.Count < pattern.tacks.Count) mesh.tackVertices.Add(new Vector2Int(-1, -1));
                    Vector2Int pair = mesh.tackVertices[t.tack];
                    if (t.first) pair.x = baseIndex + t.v; else pair.y = baseIndex + t.v;
                    mesh.tackVertices[t.tack] = pair;
                }
                // The outline's own segments, for elastic along an edge.
                for (int i = 0; i < outlineCount; i++)
                {
                    int j = (i + 1) % outlineCount;
                    if (pointEdge[i] >= 0 && pointEdge[j] >= 0) mesh.outlineSegments.Add(new Vector3Int(baseIndex + i, baseIndex + j, pointEdge[i]));
                }

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
                for (int l = 0; l < lineTotal; l++)
                {
                    List<int> vertices = lineVertices[l];
                    if (vertices == null) continue;
                    for (int i = 0; i + 1 < vertices.Count; i++)
                    {
                        if (vertices[i] != vertices[i + 1] && !edgeSet.Contains(EdgeKey(vertices[i], vertices[i + 1]))) Unrecovered(LineGeometry.Describe(piece.lines[l], l) + " on '" + piece.name + "'");
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
                        for (int k = 0; k <= down && k < la.Count && k < lb.Count; k++)
                        {
                            // Held folded, as a folded seam is (see ClothModel's folds): a stitch is otherwise a
                            // free hinge, and the fold it closes swung out over the pleat.
                            AddStitch(mesh, baseIndex + la[k], baseIndex + lb[k], group);
                            mesh.foldedStitches.Add(mesh.stitches.Count - 1);
                        }
                        group++;
                    }
                    if (!pleat.pressed) continue;
                    for (int l = 0; l < lines.Length; l++)
                    {
                        if (!s.crease[l]) continue;
                        for (int k = 0; k + 1 < lines[l].Count; k++)
                        {
                            mesh.creases.Add(new Vector2Int(baseIndex + lines[l][k], baseIndex + lines[l][k + 1]));
                            mesh.creaseAngle.Add(-1f);
                            mesh.creaseSharp.Add(pleat.sharpFolds);
                        }
                    }
                }

                var global = new List<(float f, int v)>[n + lineTotal];
                for (int e = 0; e < n + lineTotal; e++)
                {
                    global[e] = new List<(float, int)>(edgeSamples[e] != null ? edgeSamples[e].Count : 0);
                    if (edgeSamples[e] != null) foreach (var s in edgeSamples[e]) global[e].Add((s.f, baseIndex + s.v));
                }
                edgeSamplesAll[p] = global;

                LayOutPleats(mesh, p, pleats[p], curves[p]);
                CatchPleatLayers(mesh, p, pleats[p], bh, ref group);

                // Fold lines: a crease that rests folded by the line's angle, laid out a little off flat so
                // the crease turns the way it was drawn (a ridge out from the body, or a valley in).
                for (int l = 0; l < lineTotal; l++)
                {
                    List<int> vertices = lineVertices[l];
                    PatternLine line = piece.lines[l];
                    if (vertices == null || line.kind != PatternLineKind.Fold || line.foldAngle <= 0.5f) continue;
                    float nudge = LineGeometry.FoldNudgeCm * Mathf.Clamp01(line.foldAngle / 60f) * (line.foldInward ? 1f : -1f);
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        if (i + 1 < vertices.Count && vertices[i] != vertices[i + 1])
                        {
                            mesh.creases.Add(new Vector2Int(baseIndex + vertices[i], baseIndex + vertices[i + 1]));
                            mesh.creaseAngle.Add(line.foldAngle);
                            mesh.creaseSharp.Add(line.sharp);
                        }
                        // Not the ends on the outline: they are sewn or hemmed with the rest of their edge.
                        if (vertices[i] >= outlineCount) mesh.layoutDepth[baseIndex + vertices[i]] += nudge;
                    }
                }
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
                    if (pattern.seams[plan.seam].folded) mesh.foldedStitches.Add(mesh.stitches.Count - 1);
                }
            }

            // Tacks and buttons: one stitch each.
            if (pattern.tacks != null)
            {
                while (mesh.tackVertices.Count < pattern.tacks.Count) mesh.tackVertices.Add(new Vector2Int(-1, -1));
                for (int ti = 0; ti < pattern.tacks.Count; ti++)
                {
                    Vector2Int pair = mesh.tackVertices[ti];
                    if (pair.x < 0 || pair.y < 0 || pair.x == pair.y) continue;
                    AddStitch(mesh, pair.x, pair.y, group++);
                    mesh.tackStitches.Add(mesh.stitches.Count - 1);
                }
            }
            return mesh;
        }

        /// <summary>How many triangles the cloth between two of a pleat's folds needs across.</summary>
        public const float PleatEdgesPerFold = 2f;
        /// <summary>The finest spacing a pleat asks for (cm), however shallow it is.</summary>
        public const float FinestPleatEdgeCm = 0.3f;

        /// <summary>
        /// The coarsest spacing a piece's pleats allow (cm): half the shallowest pleat's depth, so the cloth
        /// between two folds is at least two triangles across. With one, the folds do not hold: measured on the
        /// shirt's 2 cm box pleat at a 2 cm mesh, an inner fold had come out over the box in every one of 10
        /// rows; at 1 cm both stayed under it in 18 of 19. No limit for a piece without pleats.
        /// </summary>
        public static float PleatEdgeCm(PatternPiece piece)
        {
            float edge = float.MaxValue;
            if (piece?.pleats == null) return edge;
            foreach (PatternPleat pleat in piece.pleats)
            {
                if (pleat == null) continue;
                edge = Mathf.Min(edge, Mathf.Max(FinestPleatEdgeCm, pleat.depthCm / PleatEdgesPerFold));
            }
            return edge;
        }

        /// <summary>
        /// Catches every layer of each pleat in the edge it opens on, as sewing that edge does: each folded
        /// layer's point on the edge is stitched to the top layer's point over it (as laid out, folded), held
        /// folded. A seam on a pleated edge skips what the pleat takes in, so the folded layers' top edges were
        /// sewn to nothing. Measured on the shirt's box pleat, stitched down 4 cm: with the stitched lines
        /// swinging freely the folds came out over the box under the yoke; held folded, most of the box showed;
        /// held folded and caught here, the box was whole to the yoke and both folds stayed under it in 19 of 19
        /// rows (18 before). A catch stitch's ends start up to three quarters of an edge apart, so the drape's
        /// widest seam gap is a little wider (4.5 against 3.4 mm), under the seam; the bake welds them.
        /// </summary>
        private static void CatchPleatLayers(PatternMesh mesh, int p, List<(PatternPleat pleat, DartGeometry.PleatShape shape)> pleats, float edgeCm, ref int group)
        {
            if (pleats.Count == 0) return;
            int start = mesh.pieceStart[p], end = start + mesh.pieceCount[p];
            var stitched = new HashSet<long>();
            foreach (Vector2Int s in mesh.stitches) stitched.Add(s.x < s.y ? ((long)s.x << 32) | (uint)s.y : ((long)s.y << 32) | (uint)s.x);
            foreach (var pl in pleats)
            {
                var top = new List<int>();
                var folded = new List<int>();
                for (int v = start; v < end; v++)
                {
                    if (mesh.boundaryEdge[v] != pl.shape.edge) continue;
                    (mesh.layoutDepth[v] > 0.01f ? folded : top).Add(v);
                }
                folded.Sort((a, b) => Vector2.Dot(mesh.layout[a], pl.shape.tangent).CompareTo(Vector2.Dot(mesh.layout[b], pl.shape.tangent)));
                bool any = false;
                foreach (int f in folded)
                {
                    int best = -1;
                    float bestDistance = edgeCm * 0.75f;
                    foreach (int t in top)
                    {
                        float d = (mesh.layout[f] - mesh.layout[t]).magnitude;
                        if (d < bestDistance) { bestDistance = d; best = t; }
                    }
                    if (best < 0) continue;
                    long key = best < f ? ((long)best << 32) | (uint)f : ((long)f << 32) | (uint)best;
                    if (!stitched.Add(key)) continue;
                    AddStitch(mesh, best, f, group);
                    mesh.foldedStitches.Add(mesh.stitches.Count - 1);
                    any = true;
                }
                if (any) group++;
            }
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
            mesh.onHole.Add(false);
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

        /// <summary>The piece and edge a seam's side is on. A line inside the piece counts as edge (edge count + line index).</summary>
        private static bool Resolve(ClothesPattern pattern, SeamSpan span, out int pieceIndex, out int edgeIndex)
        {
            pieceIndex = pattern.IndexOfPiece(span.pieceId);
            edgeIndex = pieceIndex >= 0 ? pattern.pieces[pieceIndex].IndexOfEdge(span.edgeId) : -1;
            if (edgeIndex < 0 && pieceIndex >= 0)
            {
                PatternPiece piece = pattern.pieces[pieceIndex];
                int line = piece.IndexOfLine(span.edgeId);
                if (line >= 0 && piece.lines[line].points.Count >= 2) edgeIndex = piece.points.Count + line;
            }
            return edgeIndex >= 0 && span.end > span.start;
        }

        /// <summary>
        /// The fraction of the length left (see <see cref="MapU"/>) at an edge fraction. False when the
        /// fraction is taken out by a dart or pleat, outside the span, or at the end of a part already.
        /// </summary>
        private static bool Unmap(List<Vector2> parts, float fraction, out float u)
        {
            u = 0f;
            float total = Total(parts), at = 0f;
            if (total <= 0f) return false;
            foreach (Vector2 part in parts)
            {
                if (fraction > part.x + 1e-4f && fraction < part.y - 1e-4f)
                {
                    u = (at + fraction - part.x) / total;
                    return true;
                }
                at += part.y - part.x;
            }
            return false;
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
            // An edge's last corner is the next edge's first point; a line holds its own end.
            int edgeCount = pattern.pieces[p].points.Count;
            if (e < edgeCount && f >= 1f - Eps) { edge = (e + 1) % edgeCount; f = 0f; }
            foreach (var s in samples[p][edge]) if (Mathf.Abs(s.f - f) < Eps) { result.Add(s.v); break; }
            return result;
        }
    }
}
