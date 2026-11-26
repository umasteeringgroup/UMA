using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The Pattern Canvas's editing aids. A drag is measured from where it began: each move puts back what was there when it
    /// started and applies the whole move again, so it can be kept to an axis (the toolbar's Horizontal or Vertical, or Ctrl
    /// held for the way it moved further), snapped so a moving point lines up with a point nearby (with guides), mirrored
    /// across the piece's middle (Mirror X), and cancelled with Escape. Also: Duplicate (and Ctrl+D) copies what is selected,
    /// the arrow keys nudge it, and a half-typed field is dropped when the selection changes.
    /// </summary>
    public sealed partial class PatternCanvasWindow
    {
        // ---- Settings, kept between sessions ----------------------------------------------------

        /// <summary>Moving points snap to points nearby, across and in height.</summary>
        public static bool SnapOn
        {
            get => EditorPrefs.GetBool("UMA.ClothesDesigner.Canvas.Snap", true);
            set => EditorPrefs.SetBool("UMA.ClothesDesigner.Canvas.Snap", value);
        }

        /// <summary>Moves keep to this axis.</summary>
        public static CanvasMoveAxis MoveAxis
        {
            get => (CanvasMoveAxis)EditorPrefs.GetInt("UMA.ClothesDesigner.Canvas.Axis", 0);
            set => EditorPrefs.SetInt("UMA.ClothesDesigner.Canvas.Axis", (int)value);
        }

        /// <summary>Within a piece, moving something moves its mirror image across the piece's middle too.</summary>
        public static bool MirrorX
        {
            get => EditorPrefs.GetBool("UMA.ClothesDesigner.Canvas.MirrorX", false);
            set => EditorPrefs.SetBool("UMA.ClothesDesigner.Canvas.MirrorX", value);
        }

        /// <summary>After nudging with the arrow keys, the cloth is built again once the keys have rested this long (s).</summary>
        public const double NudgeCommitDelay = 0.4;

        // ---- The move in progress ----------------------------------------------------------------

        private Vector2 dragStartCm, dragTotal;
        private int dragPieceIndex = -1;
        private string dragPieceJson;
        private readonly List<(Vector2 a, Vector2 b)> dragTackStarts = new List<(Vector2, Vector2)>();
        private readonly List<Vector2> dragPartStarts = new List<Vector2>();
        private readonly List<Vector2> dragMoving = new List<Vector2>(), dragTargets = new List<Vector2>();
        private readonly List<CanvasSnapGuide> dragGuides = new List<CanvasSnapGuide>();
        private CanvasMoveAxis dragAxis;
        // Mirror X: the piece's middle when the move began, and the moved thing's partner. A point or an edge that is its
        // own mirror image keeps to the middle (dragOnAxis), so the outline stays symmetric. A whole dart or line there is
        // only held by it (dragStickyMiddle): within snapping reach of the middle it stays on it (dragOnMiddle), further
        // it comes off; dragMiddleHome is the move across that puts it back on the middle.
        private bool dragMirroring, dragOnAxis, mirrorDartSwapped, dragStickyMiddle, dragOnMiddle;
        private float dragMirrorAxis, dragMiddleHome;
        private int mirrorPoint = -1, mirrorPoint2 = -1, mirrorEdge = -1, mirrorLine = -1, mirrorDart = -1, mirrorPleat = -1;
        private int[] mirrorLineMap;
        private double nudgeCommitAt = -1;
        private string lastSelectionKey;

        private enum SnapKind { Point, EdgeMiddle, LinePoint, DartTip, TackEnd, Part }

        private static long SnapKey(SnapKind kind, int piece, int a, int b) =>
            ((long)kind << 48) | ((long)(piece & 0xFFFF) << 32) | ((long)(a & 0xFFFF) << 16) | (long)(b & 0xFFFF);

        // ---- Starting, continuing and ending a move ---------------------------------------------

        /// <summary>
        /// Notes where a move starts: the pointer, what the move may change (the selected piece, every tack's ends and every
        /// part's place), the mirror partners, the points that move and what they may snap to.
        /// </summary>
        private void BeginMoveSession(Vector2 startCm)
        {
            dragStartCm = startCm;
            dragTotal = Vector2.zero;
            dragAxis = CanvasMoveAxis.Free;
            dragGuides.Clear();
            dragPieceIndex = selectedPiece >= 0 && selectedPiece < Pattern.pieces.Count ? selectedPiece : -1;
            dragPieceJson = dragPieceIndex >= 0 ? JsonUtility.ToJson(Pattern.pieces[dragPieceIndex]) : null;
            dragTackStarts.Clear();
            foreach (PatternTack t in Pattern.tacks) dragTackStarts.Add(t != null ? (t.a, t.b) : default);
            dragPartStarts.Clear();
            foreach (PatternPart p in Pattern.parts) dragPartStarts.Add(p != null ? p.position : default);
            FindMirrorPartners();
            CollectSnapping();
        }

        /// <summary>Puts back everything a move may have changed, as it was when it began.</summary>
        private void RestoreDragSnapshot()
        {
            if (dragPieceJson != null && dragPieceIndex >= 0 && dragPieceIndex < Pattern.pieces.Count)
                JsonUtility.FromJsonOverwrite(dragPieceJson, Pattern.pieces[dragPieceIndex]);
            for (int i = 0; i < Pattern.tacks.Count && i < dragTackStarts.Count; i++)
            {
                if (Pattern.tacks[i] == null) continue;
                Pattern.tacks[i].a = dragTackStarts[i].a;
                Pattern.tacks[i].b = dragTackStarts[i].b;
            }
            for (int i = 0; i < Pattern.parts.Count && i < dragPartStarts.Count; i++)
                if (Pattern.parts[i] != null) Pattern.parts[i].position = dragPartStarts[i];
        }

        /// <summary>The move so far: from where it began to the pointer, kept to an axis and snapped.</summary>
        private Vector2 MoveSoFar(Vector2 mouseCm, Event e)
        {
            Vector2 move = mouseCm - dragStartCm;
            dragAxis = dragOnAxis ? CanvasMoveAxis.Vertical : CanvasSnapping.AxisFor(move, MoveAxis, EditorGUI.actionKey);
            move = CanvasSnapping.Constrain(move, dragAxis);
            // Kept to the middle by Mirror X and to across by the toolbar: nothing is left to move (the label says why).
            if (dragOnAxis && MoveAxis == CanvasMoveAxis.Horizontal) move = Vector2.zero;
            Vector2 kept = move;
            // Shift held rounds a point to whole centimetres instead.
            bool roundsToGrid = e.shift && (selectedPoint >= 0 || (selectedLine >= 0 && lineHandle >= 0));
            float reach = CanvasSnapping.SnapPixels / Mathf.Max(0.01f, zoom);
            if (SnapOn && !roundsToGrid) move = CanvasSnapping.Snap(move, dragMoving, dragTargets, reach, dragAxis, dragGuides);
            else dragGuides.Clear();
            // A dart or line that sat on the middle stays on it until dragged further across than snapping reaches.
            dragOnMiddle = dragStickyMiddle && SnapOn && dragAxis != CanvasMoveAxis.Vertical && Mathf.Abs(kept.x - dragMiddleHome) <= reach;
            if (dragOnMiddle)
            {
                move.x = dragMiddleHome;
                dragGuides.RemoveAll(g => g.vertical);
            }
            dragTotal = move;
            return move;
        }

        /// <summary>Escape during a drag: everything goes back to where it was when the drag began.</summary>
        private void CancelMoveDrag()
        {
            if (!dragging) return;
            RestoreDragSnapshot();
            dragging = false;
            changedDuringDrag = false;
            dragGuides.Clear();
            if (dragGroup >= 0) Undo.CollapseUndoOperations(dragGroup);
            Undo.IncrementCurrentGroup();
            status = "Move cancelled.";
            Repaint();
        }

        /// <summary>
        /// Moves the selection by <paramref name="delta"/> (cm): a part; a tack or its end; a line or its point; a dart or a
        /// pleat; a curve handle; an edge; a point; or the whole piece with what is drawn on it. <paramref name="mouseCm"/> is
        /// where the pointer is, for what slides along an edge (an edge dart, a pleat).
        /// </summary>
        private void ApplyMove(Vector2 delta, Vector2 mouseCm, bool shift)
        {
            if (selectedPart >= 0 && selectedPart < Pattern.parts.Count) { Pattern.parts[selectedPart].position += delta; return; }
            if (selectedTack >= 0 && selectedTack < Pattern.tacks.Count)
            {
                PatternTack tack = Pattern.tacks[selectedTack];
                if (lineHandle == 0) tack.a += delta; else if (lineHandle == 1) tack.b += delta; else { tack.a += delta; tack.b += delta; }
                return;
            }
            if (selectedPiece < 0 || selectedPiece >= Pattern.pieces.Count) return;
            PatternPiece piece = Pattern.pieces[selectedPiece];
            if (selectedLine >= 0 && selectedLine < piece.lines.Count)
            {
                PatternLine line = piece.lines[selectedLine];
                if (lineHandle >= 0 && lineHandle < line.points.Count)
                {
                    Vector2 q = line.points[lineHandle] + delta;
                    line.points[lineHandle] = shift ? new Vector2(Mathf.Round(q.x), Mathf.Round(q.y)) : q;
                }
                else for (int i = 0; i < line.points.Count; i++) line.points[i] += delta;
                return;
            }
            if (selectedDart >= 0 && selectedDart < piece.darts.Count) DragDart(piece, piece.darts[selectedDart], delta, mouseCm);
            else if (selectedPleat >= 0 && selectedPleat < piece.pleats.Count) DragPleat(piece, piece.pleats[selectedPleat], mouseCm);
            else if (selectedHandle >= 0 && selectedEdge >= 0)
            {
                PatternEdge edge = piece.edges[selectedEdge];
                if (selectedHandle == 0) edge.handleA += delta; else edge.handleB += delta;
            }
            else if (selectedEdge >= 0 && selectedEdge < piece.edges.Count && selectedPoint < 0) PatternEditing.MoveEdge(piece, selectedEdge, delta);
            else if (selectedPoint >= 0 && selectedPoint < piece.points.Count)
            {
                Vector2 p = piece.points[selectedPoint].position + delta;
                piece.points[selectedPoint].position = shift ? new Vector2(Mathf.Round(p.x), Mathf.Round(p.y)) : p;
            }
            else
            {
                // The whole piece, with what is drawn on it.
                foreach (PatternPoint p in piece.points) p.position += delta;
                foreach (PatternLine l in piece.lines) for (int i = 0; i < l.points.Count; i++) l.points[i] += delta;
                foreach (PatternDart d in piece.darts) { d.tip += delta; d.tip2 += delta; }
                foreach (PatternTack t in Pattern.tacks) { if (t.pieceA == piece.id) t.a += delta; if (t.pieceB == piece.id) t.b += delta; }
                foreach (PatternPart part in Pattern.parts) if (part.pieceId == piece.id) part.position += delta;
            }
        }

        // ---- Mirror X ------------------------------------------------------------------------------------

        private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= PatternEditing.MirrorToleranceCm;

        /// <summary>The line of a piece whose points mirror a line's, with which of its points mirrors each; -1 when none.</summary>
        private static int MirrorLineOf(PatternPiece piece, int lineIndex, float axisX, out int[] map)
        {
            map = null;
            PatternLine line = piece.lines[lineIndex];
            int self = -1;
            for (int j = 0; j < piece.lines.Count; j++)
            {
                PatternLine other = piece.lines[j];
                if (other == null || other.points.Count != line.points.Count) continue;
                var found = new int[line.points.Count];
                bool all = true;
                for (int k = 0; k < line.points.Count && all; k++)
                {
                    Vector2 image = PatternEditing.Mirror(line.points[k], axisX);
                    found[k] = -1;
                    for (int m = 0; m < other.points.Count; m++) if (Near(other.points[m], image)) { found[k] = m; break; }
                    all = found[k] >= 0;
                }
                if (!all) continue;
                if (j != lineIndex) { map = found; return j; }
                self = j;
                map = found;
            }
            return self;
        }

        /// <summary>The dart that mirrors a dart: a diamond with its points reflected, an edge dart on the mirror edge; -1 when none.</summary>
        private static int MirrorDartOf(PatternPiece piece, int dartIndex, float axisX, out bool swapped)
        {
            swapped = false;
            PatternDart dart = piece.darts[dartIndex];
            int self = -1;
            int edge = dart.kind == DartKind.Edge ? piece.IndexOfEdge(dart.edgeId) : -1;
            int mirrorEdgeIndex = edge >= 0 ? PatternEditing.MirrorEdge(piece, edge, axisX) : -1;
            for (int j = 0; j < piece.darts.Count; j++)
            {
                PatternDart other = piece.darts[j];
                if (other == null || other.kind != dart.kind) continue;
                bool match;
                bool swap = false;
                if (dart.kind == DartKind.Double)
                {
                    Vector2 a = PatternEditing.Mirror(dart.tip, axisX), b = PatternEditing.Mirror(dart.tip2, axisX);
                    match = (Near(other.tip, a) && Near(other.tip2, b)) || (swap = Near(other.tip, b) && Near(other.tip2, a));
                }
                else
                {
                    match = mirrorEdgeIndex >= 0 && other.edgeId == piece.edges[mirrorEdgeIndex].id && Mathf.Abs(other.position - (1f - dart.position)) < 0.03f;
                }
                if (!match) continue;
                if (j != dartIndex) { swapped = swap; return j; }
                self = j;
            }
            return self;
        }

        /// <summary>The pleat on the mirror edge at the mirrored place; -1 when none.</summary>
        private static int MirrorPleatOf(PatternPiece piece, int pleatIndex, float axisX)
        {
            PatternPleat pleat = piece.pleats[pleatIndex];
            int edge = piece.IndexOfEdge(pleat.edgeId);
            int mirror = edge >= 0 ? PatternEditing.MirrorEdge(piece, edge, axisX) : -1;
            if (mirror < 0) return -1;
            for (int j = 0; j < piece.pleats.Count; j++)
            {
                if (j == pleatIndex) continue;
                PatternPleat other = piece.pleats[j];
                if (other != null && other.edgeId == piece.edges[mirror].id && Mathf.Abs(other.position - (1f - pleat.position)) < 0.03f) return j;
            }
            return -1;
        }

        /// <summary>Finds, with Mirror X on, what mirrors the selection across its piece's middle (as it was when the move began).</summary>
        private void FindMirrorPartners()
        {
            dragMirroring = dragOnAxis = mirrorDartSwapped = dragStickyMiddle = dragOnMiddle = false;
            dragMiddleHome = 0f;
            mirrorPoint = mirrorPoint2 = mirrorEdge = mirrorLine = mirrorDart = mirrorPleat = -1;
            mirrorLineMap = null;
            if (!MirrorX || mode != CanvasMode.Edit || dragPieceIndex < 0 || selectedPart >= 0 || selectedTack >= 0) return;
            PatternPiece piece = Pattern.pieces[dragPieceIndex];
            int n = piece.points.Count;
            float axis = dragMirrorAxis = piece.Bounds().center.x;
            if (selectedLine >= 0 && selectedLine < piece.lines.Count)
            {
                mirrorLine = MirrorLineOf(piece, selectedLine, axis, out mirrorLineMap);
                if (mirrorLine == selectedLine)
                {
                    // A line that mirrors itself (a centre front line, a yoke across the middle): a point of it on the
                    // middle keeps to the middle, so the line stays symmetric; the whole line is only held there.
                    if (lineHandle >= 0 && mirrorLineMap[lineHandle] == lineHandle) { dragOnAxis = true; mirrorLine = -1; }
                    else if (lineHandle < 0)
                    {
                        PatternLine line = piece.lines[selectedLine];
                        float x = 0f;
                        foreach (Vector2 q in line.points) x += q.x;
                        HoldOnMiddle(axis - x / Mathf.Max(1, line.points.Count));
                        mirrorLine = -1;
                    }
                }
                dragMirroring = mirrorLine >= 0 || dragOnAxis;
                return;
            }
            if (selectedDart >= 0 && selectedDart < piece.darts.Count)
            {
                mirrorDart = MirrorDartOf(piece, selectedDart, axis, out mirrorDartSwapped);
                if (mirrorDart == selectedDart)
                {
                    // A diamond dart on the middle: one of its points keeps to the middle; the whole dart is only held
                    // there, so a new dart (added on the middle) can be dragged off to where it belongs.
                    PatternDart dart = piece.darts[selectedDart];
                    if (dart.kind == DartKind.Double)
                    {
                        if (dartHandle == 0 || dartHandle == 1) dragOnAxis = true;
                        else HoldOnMiddle(axis - (dart.tip.x + dart.tip2.x) * 0.5f);
                    }
                    mirrorDart = -1;
                }
                dragMirroring = mirrorDart >= 0 || dragOnAxis;
                return;
            }
            if (selectedPleat >= 0 && selectedPleat < piece.pleats.Count)
            {
                mirrorPleat = MirrorPleatOf(piece, selectedPleat, axis);
                dragMirroring = mirrorPleat >= 0;
                return;
            }
            if (selectedHandle >= 0 && selectedEdge >= 0)
            {
                mirrorEdge = PatternEditing.MirrorEdge(piece, selectedEdge, axis);
                dragMirroring = mirrorEdge >= 0;
                return;
            }
            if (selectedEdge >= 0 && selectedEdge < n && selectedPoint < 0)
            {
                int a = selectedEdge, b = (selectedEdge + 1) % n;
                mirrorPoint = PatternEditing.MirrorPartnerPoint(piece, piece.points[a].position, axis);
                mirrorPoint2 = PatternEditing.MirrorPartnerPoint(piece, piece.points[b].position, axis);
                // An edge across the middle that mirrors itself (a hem, a neckline) moves only up and down.
                if (mirrorPoint == b && mirrorPoint2 == a) { dragOnAxis = true; mirrorPoint = mirrorPoint2 = -1; }
                if (mirrorPoint == a) mirrorPoint = -1;
                if (mirrorPoint2 == b) mirrorPoint2 = -1;
                dragMirroring = dragOnAxis || mirrorPoint >= 0 || mirrorPoint2 >= 0;
                return;
            }
            if (selectedPoint >= 0 && selectedPoint < n)
            {
                int partner = PatternEditing.MirrorPartnerPoint(piece, piece.points[selectedPoint].position, axis);
                if (partner == selectedPoint) dragOnAxis = true; else mirrorPoint = partner;
                dragMirroring = dragOnAxis || mirrorPoint >= 0;
            }
        }

        /// <summary>The moved dart or line sits on the middle: it is held there, <paramref name="home"/> across bringing it exactly onto it.</summary>
        private void HoldOnMiddle(float home)
        {
            dragStickyMiddle = true;
            dragMiddleHome = home;
        }

        /// <summary>When a move ends: a dart or line taken off the middle with Mirror X has no twin yet; the status says how to add one.</summary>
        private void NoteMoveEnded()
        {
            if (dragStickyMiddle && !dragOnMiddle && MirrorX)
                status = (selectedDart >= 0 ? "Dart" : "Line") + " moved off the middle; Mirror X has nothing to mirror it with. Ctrl+D (Duplicate) adds its mirror twin.";
            dragStickyMiddle = dragOnMiddle = false;
        }

        /// <summary>Makes <paramref name="other"/> the mirror image of <paramref name="dart"/> across <paramref name="axisX"/>: its place and its shape.</summary>
        private static void MirrorDartInto(PatternDart dart, PatternDart other, bool swapped, float axisX)
        {
            Vector2 M(Vector2 p) => PatternEditing.Mirror(p, axisX);
            if (dart.kind == DartKind.Double)
            {
                // Swapped: the twin's first point mirrors this dart's second, so its widest place counts from the other end.
                if (swapped) { other.tip = M(dart.tip2); other.tip2 = M(dart.tip); other.widestAt = 1f - dart.widestAt; }
                else { other.tip = M(dart.tip); other.tip2 = M(dart.tip2); other.widestAt = dart.widestAt; }
                other.roundness = dart.roundness;
            }
            else
            {
                other.position = 1f - dart.position;
                other.tip = M(dart.tip);
                other.curveCm = dart.curveCm;
            }
            other.widthCm = dart.widthCm;
        }

        /// <summary>With Mirror X, the side panel's dart (index <paramref name="dartIndex"/>) and its twin as they were before its fields drew.</summary>
        private int SidePanelDartTwin(PatternPiece piece, int dartIndex, out bool swapped, out float axisX)
        {
            swapped = false;
            axisX = piece.Bounds().center.x;
            if (!MirrorX || dartIndex < 0 || dartIndex >= piece.darts.Count) return -1;
            int twin = MirrorDartOf(piece, dartIndex, axisX, out swapped);
            return twin != dartIndex ? twin : -1;
        }

        /// <summary>After the side panel's fields: a change to the dart is made to its twin too (Mirror X).</summary>
        private void MirrorSidePanelDart(PatternPiece piece, PatternDart dart, string before, int twin, bool swapped, float axisX)
        {
            if (twin < 0 || twin >= piece.darts.Count || JsonUtility.ToJson(dart) == before) return;
            MirrorDartInto(dart, piece.darts[twin], swapped, axisX);
        }

        /// <summary>After the selection has moved: its mirror partner set to its mirror image.</summary>
        private void ApplyMirror()
        {
            if (!dragMirroring || dragPieceIndex < 0 || dragPieceIndex >= Pattern.pieces.Count) return;
            PatternPiece piece = Pattern.pieces[dragPieceIndex];
            float axis = dragMirrorAxis;
            int n = piece.points.Count;
            Vector2 M(Vector2 p) => PatternEditing.Mirror(p, axis);
            Vector2 MV(Vector2 v) => new Vector2(-v.x, v.y);
            if (selectedLine >= 0 && mirrorLine >= 0 && selectedLine < piece.lines.Count && mirrorLine < piece.lines.Count && mirrorLineMap != null)
            {
                PatternLine line = piece.lines[selectedLine], other = piece.lines[mirrorLine];
                for (int k = 0; k < line.points.Count && k < mirrorLineMap.Length; k++)
                    if (lineHandle < 0 || k == lineHandle) other.points[mirrorLineMap[k]] = M(line.points[k]);
                return;
            }
            if (selectedDart >= 0 && mirrorDart >= 0 && selectedDart < piece.darts.Count && mirrorDart < piece.darts.Count)
            {
                MirrorDartInto(piece.darts[selectedDart], piece.darts[mirrorDart], mirrorDartSwapped, axis);
                return;
            }
            if (selectedPleat >= 0 && mirrorPleat >= 0 && selectedPleat < piece.pleats.Count && mirrorPleat < piece.pleats.Count)
            {
                piece.pleats[mirrorPleat].position = 1f - piece.pleats[selectedPleat].position;
                return;
            }
            if (selectedHandle >= 0 && selectedEdge >= 0 && mirrorEdge >= 0)
            {
                PatternEdge edge = piece.edges[selectedEdge], other = piece.edges[mirrorEdge];
                // A mirrored edge runs the other way: its handle B mirrors handle A, and the other way round.
                if (selectedHandle == 0) other.handleB = MV(edge.handleA); else other.handleA = MV(edge.handleB);
                other.curved = edge.curved;
                return;
            }
            if (selectedEdge >= 0 && selectedPoint < 0 && selectedEdge < n)
            {
                if (mirrorPoint >= 0) piece.points[mirrorPoint].position = M(piece.points[selectedEdge].position);
                if (mirrorPoint2 >= 0) piece.points[mirrorPoint2].position = M(piece.points[(selectedEdge + 1) % n].position);
                return;
            }
            if (selectedPoint >= 0 && mirrorPoint >= 0 && selectedPoint < n && mirrorPoint < n)
                piece.points[mirrorPoint].position = M(piece.points[selectedPoint].position);
        }

        // ---- Snapping ------------------------------------------------------------------------------------

        /// <summary>The points the selection moves (as they were) and the points they may line up with: every other point,
        /// edge middle, line point, dart point, tack end and part, leaving out what moves with the selection.</summary>
        private void CollectSnapping()
        {
            dragMoving.Clear();
            dragTargets.Clear();
            var excluded = new HashSet<long>();
            bool wholePiece = false;
            int sp = dragPieceIndex;
            PatternPiece piece = sp >= 0 ? Pattern.pieces[sp] : null;
            int n = piece != null ? piece.points.Count : 0;
            void ExcludePoint(int i)
            {
                if (piece == null || i < 0 || n == 0) return;
                excluded.Add(SnapKey(SnapKind.Point, sp, i, 0));
                excluded.Add(SnapKey(SnapKind.EdgeMiddle, sp, i, 0));
                excluded.Add(SnapKey(SnapKind.EdgeMiddle, sp, (i - 1 + n) % n, 0));
            }
            void ExcludeLine(int l)
            {
                if (piece == null || l < 0 || l >= piece.lines.Count) return;
                for (int k = 0; k < piece.lines[l].points.Count; k++) excluded.Add(SnapKey(SnapKind.LinePoint, sp, l, k));
            }
            void ExcludeDart(int d) { excluded.Add(SnapKey(SnapKind.DartTip, sp, d, 0)); excluded.Add(SnapKey(SnapKind.DartTip, sp, d, 1)); }

            if (selectedPart >= 0 && selectedPart < Pattern.parts.Count)
            {
                dragMoving.Add(Pattern.parts[selectedPart].position);
                excluded.Add(SnapKey(SnapKind.Part, -1, selectedPart, 0));
            }
            else if (selectedTack >= 0 && selectedTack < Pattern.tacks.Count)
            {
                PatternTack tack = Pattern.tacks[selectedTack];
                if (lineHandle != 1) dragMoving.Add(tack.a);
                if (lineHandle != 0) dragMoving.Add(tack.b);
                excluded.Add(SnapKey(SnapKind.TackEnd, -1, selectedTack, 0));
                excluded.Add(SnapKey(SnapKind.TackEnd, -1, selectedTack, 1));
            }
            else if (piece == null) return;
            else if (selectedLine >= 0 && selectedLine < piece.lines.Count)
            {
                PatternLine line = piece.lines[selectedLine];
                if (lineHandle >= 0 && lineHandle < line.points.Count) dragMoving.Add(line.points[lineHandle]);
                else dragMoving.AddRange(line.points);
                ExcludeLine(selectedLine);
                ExcludeLine(mirrorLine);
            }
            else if (selectedDart >= 0 && selectedDart < piece.darts.Count)
            {
                PatternDart dart = piece.darts[selectedDart];
                if (dartHandle == 0 || (dart.kind == DartKind.Double && dartHandle != 1)) dragMoving.Add(dart.tip);
                if (dart.kind == DartKind.Double && dartHandle != 0) dragMoving.Add(dart.tip2);
                ExcludeDart(selectedDart);
                if (mirrorDart >= 0) ExcludeDart(mirrorDart);
            }
            else if (selectedPleat >= 0) return;
            else if (selectedHandle >= 0 && selectedEdge >= 0 && selectedEdge < n)
            {
                PatternEdge edge = piece.edges[selectedEdge];
                dragMoving.Add(selectedHandle == 0 ? piece.points[selectedEdge].position + edge.handleA : piece.points[(selectedEdge + 1) % n].position + edge.handleB);
            }
            else if (selectedEdge >= 0 && selectedEdge < n && selectedPoint < 0)
            {
                int a = selectedEdge, b = (selectedEdge + 1) % n;
                dragMoving.Add(piece.points[a].position);
                dragMoving.Add(piece.points[b].position);
                ExcludePoint(a); ExcludePoint(b); ExcludePoint(mirrorPoint); ExcludePoint(mirrorPoint2);
            }
            else if (selectedPoint >= 0 && selectedPoint < n)
            {
                dragMoving.Add(piece.points[selectedPoint].position);
                ExcludePoint(selectedPoint); ExcludePoint(mirrorPoint);
            }
            else
            {
                foreach (PatternPoint p in piece.points) dragMoving.Add(p.position);
                wholePiece = true;
            }

            for (int pi = 0; pi < Pattern.pieces.Count; pi++)
            {
                if (wholePiece && pi == sp) continue;
                PatternPiece other = Pattern.pieces[pi];
                int count = other.points.Count;
                for (int i = 0; i < count; i++)
                {
                    if (!excluded.Contains(SnapKey(SnapKind.Point, pi, i, 0))) dragTargets.Add(other.points[i].position);
                    if (!excluded.Contains(SnapKey(SnapKind.EdgeMiddle, pi, i, 0))) dragTargets.Add((other.points[i].position + other.points[(i + 1) % count].position) * 0.5f);
                }
                for (int l = 0; l < other.lines.Count; l++)
                    for (int k = 0; k < other.lines[l].points.Count; k++)
                        if (!excluded.Contains(SnapKey(SnapKind.LinePoint, pi, l, k))) dragTargets.Add(other.lines[l].points[k]);
                for (int d = 0; d < other.darts.Count; d++)
                {
                    if (!excluded.Contains(SnapKey(SnapKind.DartTip, pi, d, 0))) dragTargets.Add(other.darts[d].tip);
                    if (other.darts[d].kind == DartKind.Double && !excluded.Contains(SnapKey(SnapKind.DartTip, pi, d, 1))) dragTargets.Add(other.darts[d].tip2);
                }
            }
            string movingId = wholePiece ? piece.id : null;
            for (int t = 0; t < Pattern.tacks.Count; t++)
            {
                PatternTack tack = Pattern.tacks[t];
                if (tack == null) continue;
                if (tack.pieceA != movingId && !excluded.Contains(SnapKey(SnapKind.TackEnd, -1, t, 0))) dragTargets.Add(tack.a);
                if (tack.pieceB != movingId && !excluded.Contains(SnapKey(SnapKind.TackEnd, -1, t, 1))) dragTargets.Add(tack.b);
            }
            for (int p = 0; p < Pattern.parts.Count; p++)
            {
                PatternPart part = Pattern.parts[p];
                if (part != null && part.pieceId != movingId && !excluded.Contains(SnapKey(SnapKind.Part, -1, p, 0))) dragTargets.Add(part.position);
            }
        }

        // ---- Drawing the aids ------------------------------------------------------------------------

        private static readonly Color GuideColor = new Color(1f, 0.3f, 0.85f, 0.95f);
        private static readonly Color MirrorAxisColor = new Color(0.45f, 0.85f, 1f, 0.75f);

        /// <summary>The Mirror X axis of the selected piece, and during a move: the axis it keeps to, the snap guides, and how far it has moved.</summary>
        private void DrawMoveAids()
        {
            if (Event.current.type != EventType.Repaint || mode != CanvasMode.Edit) return;
            Handles.BeginGUI();
            Color previous = Handles.color;
            if (MirrorX && selectedPiece >= 0 && selectedPiece < Pattern.pieces.Count)
            {
                Rect b = Pattern.pieces[selectedPiece].Bounds();
                float axis = dragging && (dragMirroring || dragStickyMiddle) ? dragMirrorAxis : b.center.x;
                // Held on the middle: the axis is drawn as a snap guide.
                bool held = dragging && dragOnMiddle;
                Handles.color = held ? GuideColor : MirrorAxisColor;
                Handles.DrawDottedLine(ToScreen(new Vector2(axis, b.yMin - 4f)), ToScreen(new Vector2(axis, b.yMax + 4f)), held ? 2f : 4f);
            }
            if (dragging)
            {
                if (dragAxis != CanvasMoveAxis.Free && dragMoving.Count > 0)
                {
                    Vector2 s = ToScreen(dragMoving[0]);
                    Vector2 along = dragAxis == CanvasMoveAxis.Horizontal ? Vector2.right : Vector2.up;
                    Handles.color = new Color(1f, 1f, 1f, 0.3f);
                    Handles.DrawLine(s - along * 4000f, s + along * 4000f);
                }
                foreach (CanvasSnapGuide g in dragGuides)
                {
                    Vector2 a = ToScreen(g.moving), t = ToScreen(g.target);
                    // Past both points a little, so the line reads as a guide.
                    Vector2 dir = (t - a).sqrMagnitude > 1e-4f ? (t - a).normalized : (g.vertical ? Vector2.up : Vector2.right);
                    Handles.color = GuideColor;
                    Handles.DrawDottedLine(a - dir * 12f, t + dir * 12f, 3f);
                    Handles.DrawSolidDisc(t, Vector3.forward, 3.5f);
                }
                string axisText = dragOnAxis
                    ? (MoveAxis == CanvasMoveAxis.Horizontal ? "  (on the middle: Mirror X keeps it there and Horizontal allows no other move)" : "  (on the middle: Mirror X keeps it there, up and down only)")
                    : dragOnMiddle ? "  (held on the middle; drag further across to take it off)"
                    : dragAxis == CanvasMoveAxis.Horizontal ? "  (across only)" : dragAxis == CanvasMoveAxis.Vertical ? "  (up and down only)" : "";
                string text = "Δ " + dragTotal.x.ToString("0.0") + ", " + dragTotal.y.ToString("0.0") + " cm" + axisText + (dragGuides.Count > 0 ? "  snapped" : "");
                Vector2 mouse = Event.current.mousePosition;
                var content = new GUIContent(text);
                Vector2 size = EditorStyles.helpBox.CalcSize(content);
                GUI.Label(new Rect(mouse.x + 16f, mouse.y + 12f, size.x + 4f, size.y), content, EditorStyles.helpBox);
            }
            Handles.color = previous;
            Handles.EndGUI();
        }

        // ---- Toolbar, duplicate, keys and nudging -------------------------------------------------------

        /// <summary>The move aids on the toolbar (Edit mode): Snap, the axis moves keep to, and Mirror X.</summary>
        private void DrawMoveToolbar()
        {
            if (mode != CanvasMode.Edit && mode != CanvasMode.Draw) return;
            GUILayout.Space(8f);
            bool snap = GUILayout.Toggle(SnapOn, new GUIContent("Snap", "While moving, a moving point lines up with points nearby (corners, edge middles, line and dart points), across and in height, with a guide. While drawing, a click lands on a point nearby on the piece, or lines up with points elsewhere. Shift rounds to whole centimetres instead."), EditorStyles.toolbarButton);
            if (snap != SnapOn) SnapOn = snap;
            var axis = (CanvasMoveAxis)GUILayout.Toolbar((int)MoveAxis, new[]
            {
                new GUIContent("Free", "Moves go any way. Hold Ctrl (Cmd on a Mac) while dragging, or drawing, to keep to the way you moved further from the last point."),
                new GUIContent("Horizontal", "Moves go across only; drawn points stay level with the last one."),
                new GUIContent("Vertical", "Moves go up and down only; drawn points stay upright from the last one.")
            }, EditorStyles.toolbarButton, GUILayout.Width(190f));
            if (axis != MoveAxis) MoveAxis = axis;
            if (mode != CanvasMode.Edit) return;
            bool mirror = GUILayout.Toggle(MirrorX, new GUIContent("Mirror X", "Within a piece, moving a point, an edge, a curve, a line, a dart or a pleat moves its mirror image across the piece's middle (the dotted line) too. A point or an edge on the middle stays on it (up and down only); a dart or a line on the middle is held there but can be dragged off. Add Diamond Dart adds a mirrored pair, side-panel changes to a dart reach its twin, and Duplicate places the copy mirrored."), EditorStyles.toolbarButton);
            if (mirror != MirrorX) { MirrorX = mirror; Repaint(); }
        }

        /// <summary>
        /// Duplicate: a copy of what is selected (a dart, a pleat, a line or a part), beside it or, with Mirror X, mirrored
        /// across its piece's middle; with only a piece selected, the piece.
        /// </summary>
        private void DuplicateSelection()
        {
            if (selectedPart >= 0 && selectedPart < Pattern.parts.Count)
            {
                Record("Duplicate Part");
                PatternPart part = Pattern.parts[selectedPart];
                PatternPiece on = Pattern.FindPiece(part.pieceId);
                PatternEditing.DuplicatePart(Pattern, part, MirrorX && on != null ? on.Bounds().center.x : (float?)null);
                selectedPart = Pattern.parts.Count - 1;
                status = "Part duplicated" + (MirrorX ? ", mirrored." : ".");
                CommitIfChanged();
                return;
            }
            if (selectedPiece < 0 || selectedPiece >= Pattern.pieces.Count) return;
            PatternPiece piece = Pattern.pieces[selectedPiece];
            float? axis = MirrorX ? piece.Bounds().center.x : (float?)null;
            string how = MirrorX ? ", mirrored across the piece's middle." : ".";
            if (selectedDart >= 0 && selectedDart < piece.darts.Count)
            {
                Record("Duplicate Dart");
                if (PatternEditing.DuplicateDart(piece, piece.darts[selectedDart], axis) != null) { SelectDart(piece.darts.Count - 1); status = "Dart duplicated" + how; }
                CommitIfChanged();
                return;
            }
            if (selectedPleat >= 0 && selectedPleat < piece.pleats.Count)
            {
                Record("Duplicate Pleat");
                if (PatternEditing.DuplicatePleat(piece, piece.pleats[selectedPleat], axis) != null) { SelectPleat(piece.pleats.Count - 1); status = "Pleat duplicated" + how; }
                CommitIfChanged();
                return;
            }
            if (selectedLine >= 0 && selectedLine < piece.lines.Count)
            {
                Record("Duplicate Line");
                PatternEditing.DuplicateLine(piece, piece.lines[selectedLine], axis);
                SelectLine(selectedPiece, piece.lines.Count - 1);
                status = "Line duplicated" + how;
                CommitIfChanged();
                return;
            }
            Record("Duplicate Piece");
            Pattern.pieces.Add(PatternEditing.Duplicate(piece, piece.name + " Copy", new Vector2(piece.Bounds().width + 10f, 0f)));
            selectedPiece = Pattern.pieces.Count - 1;
            selectedPoint = -1; selectedEdge = -1; selectedHandle = -1;
            status = "Piece duplicated.";
            CommitIfChanged();
        }

        /// <summary>
        /// Keys the canvas takes while no field is being typed in: Escape cancels a move; Ctrl+D (Cmd+D) duplicates; the
        /// arrow keys nudge what is selected a millimetre (Shift: a centimetre).
        /// </summary>
        private bool HandleEditingKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return false;
            if (dragging && e.keyCode == KeyCode.Escape) { CancelMoveDrag(); e.Use(); return true; }
            if (mode != CanvasMode.Edit || EditorGUIUtility.editingTextField || dragging) return false;
            if (e.keyCode == KeyCode.D && EditorGUI.actionKey) { DuplicateSelection(); e.Use(); Repaint(); return true; }
            Vector2 step = e.keyCode == KeyCode.LeftArrow ? Vector2.left : e.keyCode == KeyCode.RightArrow ? Vector2.right
                : e.keyCode == KeyCode.UpArrow ? Vector2.up : e.keyCode == KeyCode.DownArrow ? Vector2.down : Vector2.zero;
            if (step == Vector2.zero || !HasMovableSelection()) return false;
            Nudge(step * (e.shift ? 1f : 0.1f));
            e.Use();
            return true;
        }

        private bool HasMovableSelection() =>
            (selectedPart >= 0 && selectedPart < Pattern.parts.Count) || (selectedTack >= 0 && selectedTack < Pattern.tacks.Count)
            || (selectedPiece >= 0 && selectedPiece < Pattern.pieces.Count);

        /// <summary>Moves the selection by a step (cm), mirrored with Mirror X; the cloth is built again once the keys rest.</summary>
        private void Nudge(Vector2 step)
        {
            Record("Nudge");
            Vector2 anchor = Vector2.zero;
            // An edge dart slides along its edge: from its middle, as a drag from there would.
            if (selectedPiece >= 0 && selectedPiece < Pattern.pieces.Count && selectedDart >= 0 && selectedDart < Pattern.pieces[selectedPiece].darts.Count)
            {
                PatternPiece piece = Pattern.pieces[selectedPiece];
                PatternDart dart = piece.darts[selectedDart];
                int edge = dart.kind == DartKind.Edge ? piece.IndexOfEdge(dart.edgeId) : -1;
                if (edge >= 0) anchor = new PatternGeometry.EdgeCurve(piece, edge, PieceScale.Identity).At(dart.position);
            }
            BeginMoveSession(anchor);
            if (dragOnAxis) step.x = 0f;
            ApplyMove(step, anchor + step, false);
            ApplyMirror();
            nudgeCommitAt = EditorApplication.timeSinceStartup + NudgeCommitDelay;
            Repaint();
        }

        private void Update()
        {
            if (nudgeCommitAt < 0 || EditorApplication.timeSinceStartup < nudgeCommitAt || Pattern == null) return;
            nudgeCommitAt = -1;
            CommitIfChanged();
            Repaint();
        }

        // ---- Fields typed in when the selection changes -------------------------------------------------

        private string SelectionKey() =>
            (int)mode + "|" + selectedPiece + "|" + selectedPoint + "|" + selectedEdge + "|" + selectedHandle + "|" + selectedDart + "|" + selectedPleat
            + "|" + selectedLine + "|" + selectedTack + "|" + selectedPart + "|" + selectedTrim;

        /// <summary>
        /// Drops a half-typed field when what it belongs to is no longer selected. The side panel's fields for one dart, edge
        /// or piece are the same controls for the next, so a value typed and not entered stayed in the field, showed against
        /// the newly selected thing and went into it: measured 2026-11-25, 9 typed into one diamond dart's width without
        /// Return, the other dart picked, 6 typed: its width became 96, and the field seemed not to take the keys.
        /// </summary>
        private void EndTypingOnSelectionChange()
        {
            string key = SelectionKey();
            if (key == lastSelectionKey) return;
            bool first = lastSelectionKey == null;
            lastSelectionKey = key;
            if (first || !EditorGUIUtility.editingTextField) return;
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }
    }
}
