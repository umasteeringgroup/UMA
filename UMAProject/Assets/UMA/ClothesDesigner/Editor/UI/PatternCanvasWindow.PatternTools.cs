using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The Pattern Canvas's pattern tools: snapping while drawing, the knife along a path, and the piece tools
    /// (Symmetrize, Cut in Half, Duplicate with Seams, Duplicate Sewn Round, Merge Points, Cut Along Line).
    /// </summary>
    public sealed partial class PatternCanvasWindow
    {
        /// <summary>The knife cuts along a path of clicked points (finished with a double-click or Enter), not straight across.</summary>
        public static bool KnifePath
        {
            get => EditorPrefs.GetBool("UMA.ClothesDesigner.Canvas.KnifePath", false);
            set => EditorPrefs.SetBool("UMA.ClothesDesigner.Canvas.KnifePath", value);
        }

        /// <summary>A knife path curves smoothly through its points instead of running straight from one to the next.</summary>
        public static bool KnifeSmooth
        {
            get => EditorPrefs.GetBool("UMA.ClothesDesigner.Canvas.KnifeSmooth", true);
            set => EditorPrefs.SetBool("UMA.ClothesDesigner.Canvas.KnifeSmooth", value);
        }

        // ---- Snapping while drawing ---------------------------------------------------------------

        private readonly List<Vector2> drawTargetsOnPiece = new List<Vector2>(), drawTargetsAll = new List<Vector2>(), drawMoving = new List<Vector2>();
        private readonly List<CanvasSnapGuide> drawGuides = new List<CanvasSnapGuide>();
        private string drawTargetsKey, drawTargetsJson, drawSnapKey, drawSnapJson;
        private Vector2 drawSnapAt;
        private int drawSnapPiece = -1, drawSnapEdge = -1;
        private float drawSnapFraction;
        private bool drawSnapOnPoint;

        /// <summary>
        /// Where a click in Draw mode lands. With Snap on: on a point nearby on the piece being drawn on (a corner, an
        /// edge's middle, a line, dart or tack point, a part, a point of what is being drawn); else exactly on the outline
        /// when the pointer is on it; else lined up across and in height with points elsewhere (with guides). From the
        /// last point, Ctrl (Cmd) or the toolbar's Horizontal or Vertical keep the new point in line with it, onto the
        /// outline as well. Shift rounds to whole centimetres instead of snapping.
        /// </summary>
        private Vector2 DrawSnap(Vector2 mouse, bool shift, bool ctrl, out int piece, out int edge, out float fraction, out bool onPoint)
        {
            Vector2 origin = ToScreen(Vector2.zero);
            string key = mouse.x + "," + mouse.y + "|" + shift + ctrl + "|" + drawPoints.Count + "|" + drawPiece + "|" + tackFromPiece + "|" + (int)drawTool
                + "|" + zoom + "|" + origin.x + "," + origin.y + "|" + SnapOn + (int)MoveAxis + "|" + Pattern.pieces.Count;
            if (key == drawSnapKey && ReferenceEquals(drawSnapJson, lastJson))
            {
                piece = drawSnapPiece; edge = drawSnapEdge; fraction = drawSnapFraction; onPoint = drawSnapOnPoint;
                return drawSnapAt;
            }
            Pick(mouse, out piece, out _, out edge, out fraction, out _);
            Vector2 at = ToCm(mouse);
            onPoint = false;
            drawGuides.Clear();
            int on = drawPiece >= 0 ? drawPiece : piece;
            float reach = CanvasSnapping.SnapPixels / Mathf.Max(0.01f, zoom);
            // From the last point: kept in line with it (Ctrl, or the toolbar's axis).
            CanvasMoveAxis axis = CanvasMoveAxis.Free;
            bool fromLast = drawPoints.Count > 0 && drawTool != DrawTool.Tack && drawTool != DrawTool.Part;
            Vector2 last = fromLast ? drawPoints[drawPoints.Count - 1] : Vector2.zero;
            if (fromLast) axis = CanvasSnapping.AxisFor(at - last, MoveAxis, ctrl);

            if (SnapOn && !shift && axis == CanvasMoveAxis.Free && on >= 0 && on < Pattern.pieces.Count)
            {
                CollectDrawTargets(on);
                float best = reach;
                foreach (Vector2 q in drawTargetsOnPiece)
                {
                    float d = (q - at).magnitude;
                    if (d <= best) { best = d; at = q; onPoint = true; }
                }
                if (onPoint) { if (piece < 0) piece = on; edge = -1; }
            }
            if (!onPoint)
            {
                if (edge >= 0 && piece >= 0)
                {
                    var curve = new PatternGeometry.EdgeCurve(Pattern.pieces[piece], edge, PieceScale.Identity);
                    at = curve.At(fraction);
                    // In line with the last point and on the outline: where the line through it crosses this edge.
                    if (axis != CanvasMoveAxis.Free && AxisMeetsEdge(curve, last, axis, ToCm(mouse), reach * 3f, out Vector2 meet)) at = meet;
                }
                else
                {
                    if (fromLast && axis != CanvasMoveAxis.Free) at = last + CanvasSnapping.Constrain(at - last, axis);
                    if (shift)
                    {
                        at = new Vector2(Mathf.Round(at.x), Mathf.Round(at.y));
                        if (fromLast && axis != CanvasMoveAxis.Free) at = last + CanvasSnapping.Constrain(at - last, axis);
                    }
                    else if (SnapOn)
                    {
                        CollectDrawTargets(on);
                        drawMoving.Clear();
                        drawMoving.Add(at);
                        at += CanvasSnapping.Snap(Vector2.zero, drawMoving, drawTargetsAll, reach, axis, drawGuides);
                    }
                }
            }
            drawSnapKey = key; drawSnapJson = lastJson;
            drawSnapAt = at; drawSnapPiece = piece; drawSnapEdge = edge; drawSnapFraction = fraction; drawSnapOnPoint = onPoint;
            return at;
        }

        /// <summary>Where the upright or level line through <paramref name="from"/> crosses an edge, nearest the pointer (within <paramref name="reach"/>).</summary>
        private static bool AxisMeetsEdge(PatternGeometry.EdgeCurve curve, Vector2 from, CanvasMoveAxis axis, Vector2 pointer, float reach, out Vector2 meet)
        {
            meet = pointer;
            Vector2 dir = axis == CanvasMoveAxis.Horizontal ? Vector2.right : Vector2.up;
            Vector2 a = from - dir * 10000f, b = from + dir * 10000f;
            const int steps = 96;
            float best = reach;
            bool found = false;
            Vector2 previous = curve.At(0f);
            for (int k = 1; k <= steps; k++)
            {
                Vector2 next = curve.At(k / (float)steps);
                if (LineGeometry.SegmentIntersection(previous, next, a, b, out float t, out _))
                {
                    Vector2 q = Vector2.Lerp(previous, next, t);
                    float d = (q - pointer).magnitude;
                    if (d <= best) { best = d; meet = q; found = true; }
                }
                previous = next;
            }
            return found;
        }

        /// <summary>
        /// What a click may snap to: on <paramref name="pieceIndex"/>, its corners, the middles of its edges (on the curve),
        /// its line and dart points, tack ends and parts, and the points drawn so far; for lining up, the same on every piece.
        /// </summary>
        private void CollectDrawTargets(int pieceIndex)
        {
            string key = pieceIndex + "|" + drawPoints.Count;
            if (key == drawTargetsKey && ReferenceEquals(drawTargetsJson, lastJson)) return;
            drawTargetsKey = key; drawTargetsJson = lastJson;
            drawTargetsOnPiece.Clear();
            drawTargetsAll.Clear();
            for (int p = 0; p < Pattern.pieces.Count; p++)
            {
                PatternPiece pc = Pattern.pieces[p];
                if (pc == null) continue;
                List<Vector2> into = p == pieceIndex ? drawTargetsOnPiece : drawTargetsAll;
                for (int i = 0; i < pc.points.Count; i++)
                {
                    into.Add(pc.points[i].position);
                    into.Add(new PatternGeometry.EdgeCurve(pc, i, PieceScale.Identity).At(0.5f));
                }
                foreach (PatternLine l in pc.lines) into.AddRange(l.points);
                foreach (PatternDart d in pc.darts)
                {
                    into.Add(d.tip);
                    if (d.kind == DartKind.Double) into.Add(d.tip2);
                }
                foreach (PatternTack t in Pattern.tacks)
                {
                    if (t == null) continue;
                    if (t.pieceA == pc.id) into.Add(t.a);
                    if (t.pieceB == pc.id) into.Add(t.b);
                }
                foreach (PatternPart part in Pattern.parts) if (part != null && part.pieceId == pc.id) into.Add(part.position);
            }
            drawTargetsOnPiece.AddRange(drawPoints);
            drawTargetsAll.AddRange(drawTargetsOnPiece);
        }

        /// <summary>The snapping while drawing: guides to what the point lines up with, and a ring where a click would land.</summary>
        private void DrawSnapAids(Vector2 cursor, bool onPoint, bool overPiece)
        {
            foreach (CanvasSnapGuide g in drawGuides)
            {
                Vector2 a = ToScreen(g.moving), t = ToScreen(g.target);
                Vector2 dir = (t - a).sqrMagnitude > 1e-4f ? (t - a).normalized : (g.vertical ? Vector2.up : Vector2.right);
                Handles.color = GuideColor;
                Handles.DrawDottedLine(a - dir * 12f, t + dir * 12f, 3f);
                Handles.DrawSolidDisc(t, Vector3.forward, 3.5f);
            }
            if (onPoint || drawGuides.Count > 0)
            {
                Handles.color = GuideColor;
                Handles.DrawWireDisc(cursor, Vector3.forward, 6f);
                Handles.DrawWireDisc(cursor, Vector3.forward, 6.5f);
            }
            else if (overPiece)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.6f);
                Handles.DrawWireDisc(cursor, Vector3.forward, 3.5f);
            }
        }

        /// <summary>Points along a path curving through its points, as the smooth knife cuts it (Catmull-Rom).</summary>
        private static List<Vector2> SmoothPath(List<Vector2> path, int perSegment)
        {
            var result = new List<Vector2>();
            if (path.Count < 3) { result.AddRange(path); return result; }
            Vector2 Tangent(int j) => j <= 0 ? path[1] - path[0] : j >= path.Count - 1 ? path[path.Count - 1] - path[path.Count - 2] : (path[j + 1] - path[j - 1]) * 0.5f;
            for (int j = 0; j + 1 < path.Count; j++)
            {
                Vector2 p0 = path[j], p3 = path[j + 1], p1 = p0 + Tangent(j) / 3f, p2 = p3 - Tangent(j + 1) / 3f;
                for (int k = 0; k < perSegment; k++) result.Add(PatternGeometry.Bezier(p0, p1, p2, p3, k / (float)perSegment));
            }
            result.Add(path[path.Count - 1]);
            return result;
        }

        // ---- Piece tools ------------------------------------------------------------------------------

        /// <summary>The upright line the piece tools work about: the selected point's, or the piece's middle.</summary>
        private float ToolAxis(PatternPiece piece, out string what)
        {
            if (selectedPiece >= 0 && selectedPiece < Pattern.pieces.Count && Pattern.pieces[selectedPiece] == piece && selectedPoint >= 0 && selectedPoint < piece.points.Count)
            {
                what = "the selected point";
                return piece.points[selectedPoint].position.x;
            }
            what = "the piece's middle";
            return piece.Bounds().center.x;
        }

        /// <summary>The piece panel's tools: Symmetrize, Cut in Half, Duplicate with Seams and Duplicate Sewn Round.</summary>
        private void DrawPieceTools(PatternPiece piece)
        {
            GUILayout.Space(4f);
            GUILayout.Label("Piece Tools", EditorStyles.boldLabel);
            float axis = ToolAxis(piece, out string axisName);
            using (new EditorGUI.DisabledScope(piece.mirrorLinked))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Symmetrize", "Makes the piece the mirror image of one of its halves, still one piece. Seams on the other half move to the edge now lying where theirs did; its darts, pleats and lines give way to the mirrored ones."), GUILayout.Width(80f));
                if (GUILayout.Button(new GUIContent("Left onto Right", "The left half is kept and mirrored onto the right."), EditorStyles.miniButtonLeft)) RunHalfTool(piece, axis, true, false);
                if (GUILayout.Button(new GUIContent("Right onto Left", "The right half is kept and mirrored onto the left."), EditorStyles.miniButtonRight)) RunHalfTool(piece, axis, false, false);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Cut in Half", "Cuts the piece along the line and makes the other half a linked mirror of the kept one, sewn to it along the cut: edit one half and the other follows."), GUILayout.Width(80f));
                if (GUILayout.Button(new GUIContent("Keep Left", "The left half is kept; the right becomes its linked mirror."), EditorStyles.miniButtonLeft)) RunHalfTool(piece, axis, true, true);
                if (GUILayout.Button(new GUIContent("Keep Right", "The right half is kept; the left becomes its linked mirror."), EditorStyles.miniButtonRight)) RunHalfTool(piece, axis, false, true);
                EditorGUILayout.EndHorizontal();
                GUILayout.Label("About the upright line at x " + axis.ToString("0.0") + " cm (" + axisName + ").", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Duplicate with Seams", "A copy sewn as this piece is, to the copies of the pieces it is sewn to (made the same way, before or after): duplicate a set of pieces one by one and the copies come out sewn together as the originals are."), EditorStyles.miniButtonLeft)) DuplicatePieceSewn(piece, false);
            if (GUILayout.Button(new GUIContent("Duplicate Sewn Round", "A copy on the layer above, sewn to this piece all round where it is free (its openings too) and to the copies of its partners: a two-ply collar, cuff, flap or belt; with every piece of a garment, a lined garment (the copies outside, the originals the lining)."), EditorStyles.miniButtonRight)) DuplicatePieceSewn(piece, true);
            EditorGUILayout.EndHorizontal();
        }

        private void ClearSubSelection()
        {
            selectedPoint = -1; selectedEdge = -1; selectedHandle = -1; selectedDart = -1; selectedPleat = -1; selectedLine = -1; dartHandle = -1; lineHandle = -1;
        }

        /// <summary>Symmetrize (<paramref name="half"/> false) or Cut in Half, keeping the left or the right.</summary>
        private void RunHalfTool(PatternPiece piece, float axis, bool keepLeft, bool half)
        {
            Record(half ? "Cut in Half" : "Symmetrize");
            string id = piece.id, name = piece.name;
            string problem;
            int dropped;
            PatternPiece result = half ? PatternEditing.CutInHalf(Pattern, piece, axis, keepLeft, out problem, out dropped) : PatternEditing.Symmetrize(Pattern, piece, axis, keepLeft, out problem, out dropped);
            if (result == null)
            {
                Undo.RevertAllInCurrentGroup();
                status = problem;
            }
            else
            {
                PatternPiece kept = Pattern.FindPiece(id);
                selectedPiece = kept != null ? Pattern.pieces.IndexOf(kept) : -1;
                ClearSubSelection();
                string side = keepLeft ? "left" : "right", other = keepLeft ? "right" : "left";
                status = half
                    ? "'" + name + "' cut in half: the " + other + " is now '" + result.name + "', a linked mirror of the " + side + ", sewn to it along the middle."
                    : "'" + name + "' made symmetric: its " + side + " half mirrored onto the " + other + ".";
                if (dropped > 0) status += " " + dropped + (dropped == 1 ? " seam" : " seams") + " on the " + other + " had no edge in the same place and " + (dropped == 1 ? "was" : "were") + " removed.";
                if (!string.IsNullOrEmpty(problem)) status += " " + problem;
                CommitIfChanged();
            }
            Repaint();
            GUIUtility.ExitGUI();
        }

        /// <summary>Duplicate with Seams, or Duplicate Sewn Round.</summary>
        private void DuplicatePieceSewn(PatternPiece piece, bool round)
        {
            Record(round ? "Duplicate Sewn Round" : "Duplicate with Seams");
            // Every copy goes the same way, clear of the originals, so copies of pieces that touch touch the same way
            // (pieces with one placement that touch on the canvas are laid on the body as one).
            float xMin = float.MaxValue, xMax = float.MinValue;
            foreach (PatternPiece p in Pattern.pieces)
            {
                if (p == null || !string.IsNullOrEmpty(p.duplicateOf) || p.points.Count == 0) continue;
                Rect pb = p.Bounds();
                xMin = Mathf.Min(xMin, pb.xMin);
                xMax = Mathf.Max(xMax, pb.xMax);
            }
            var offset = new Vector2(xMax > xMin ? xMax - xMin + 10f : piece.Bounds().width + 10f, 0f);
            int sewn, waiting, sewnRound = 0;
            PatternPiece copy = round
                ? PatternEditing.DuplicateSewnRound(Pattern, piece, piece.name + " Outer", offset, out sewnRound, out sewn, out waiting)
                : PatternEditing.DuplicateWithSeams(Pattern, piece, piece.name + " Copy", offset, out sewn, out waiting);
            selectedPiece = Pattern.pieces.IndexOf(copy);
            ClearSubSelection();
            status = "'" + copy.name + "' added" + (round ? " on the layer above, sewn to '" + piece.name + "' along " + sewnRound + (sewnRound == 1 ? " stretch" : " stretches") + " of its outline" : "")
                + (sewn > 0 ? (round ? ", and" : ",") + " sewn as '" + piece.name + "' is to " + sewn + (sewn == 1 ? " copy" : " copies") + " of its partners" : "")
                + (waiting > 0 ? "; " + waiting + (waiting == 1 ? " seam waits" : " seams wait") + " for its other piece to be duplicated the same way." : ".");
            CommitIfChanged();
            Repaint();
            GUIUtility.ExitGUI();
        }

        /// <summary>Stitch Points: the selected edge's two points become one.</summary>
        private void MergeSelectedEdgePoints(PatternPiece piece)
        {
            Record("Merge Points");
            if (PatternEditing.MergePoints(Pattern, piece, selectedEdge))
            {
                ClearSubSelection();
                status = "The edge's points are one now; the edges either side keep what is sewn to them.";
            }
            else status = "A piece needs at least three points.";
            CommitIfChanged();
            Repaint();
            GUIUtility.ExitGUI();
        }

        /// <summary>Cuts the selected line's piece along it, sewn back together.</summary>
        private void CutAlongSelectedLine(PatternPiece piece, PatternLine line)
        {
            Record("Cut Along Line");
            string name = piece.name;
            PatternPiece half = PatternEditing.CutAlongLine(Pattern, piece, line, true, out string problem);
            if (half == null)
            {
                Undo.RevertAllInCurrentGroup();
                status = problem;
            }
            else
            {
                ClearSubSelection();
                status = "Cut along the line into '" + piece.name + "' and '" + half.name + "', sewn back together along it (unsew it to part them).";
                CommitIfChanged();
            }
            Repaint();
            GUIUtility.ExitGUI();
        }
    }
}
