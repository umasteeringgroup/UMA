using System;
using System.Collections.Generic;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The 2D pattern canvas: draw the flat pieces in centimetres, curve and split edges, sew edges,
    /// mark openings, set the grain, mirror pieces, size them with dimensions, and see the UV layout.
    /// It edits the pattern open in the Clothes Designer; the cloth is rebuilt after each change.
    /// </summary>
    public sealed partial class PatternCanvasWindow : EditorWindow
    {
        private enum CanvasMode { Edit, Sew, Draw, UVs }
        private enum DrawTool { Line, Fold, Hole, Tack, Cut, Part }
        private static readonly string[] drawToolNames = { "Seam Line", "Fold Line", "Hole", "Tack / Button", "Cut Piece", "Part" };
        private int selectedPart = -1;
        private static readonly Color PartColor = new Color(0.95f, 0.95f, 0.5f);
        private DrawTool drawTool;
        private readonly List<Vector2> drawPoints = new List<Vector2>();
        private int drawPiece = -1;
        private int tackFromPiece = -1;
        private Vector2 tackFrom;
        private bool newTackIsButton;
        private bool cutAndSew = true;
        private int selectedLine = -1, selectedTack = -1;
        private int lineHandle = -1; // a point of the selected line, or -2 the whole line; a tack's end (0, 1)
        private readonly List<(int piece, int edge)> sewTargets = new List<(int, int)>();
        private int sewFromLine = -1;
        private int attachTarget = -1;
        private Vector2 attachAt;
        private static readonly Color LineColor = new Color(0.55f, 1f, 0.55f);
        private static readonly Color FoldColor = new Color(0.7f, 0.8f, 1f);
        private static readonly Color HoleColor = new Color(1f, 0.6f, 0.3f);
        private static readonly Color TackColor = new Color(1f, 0.4f, 0.9f);

        private DesignerSession session;
        private Vector2 pan = new Vector2(40f, -40f);
        private float zoom = 4f; // pixels per centimetre
        private CanvasMode mode = CanvasMode.Edit;
        private int selectedPiece = -1;
        private int selectedPoint = -1;
        private int selectedEdge = -1;
        private int selectedHandle = -1; // 0 handle A, 1 handle B of the selected edge
        private int selectedDart = -1, selectedPleat = -1; // on the selected piece
        private int dartHandle = -1; // 0 the (first) point, 1 the second point, 2 the whole dart or pleat
        private static readonly Color DartColor = new Color(1f, 0.55f, 0.85f);
        private static readonly Color PleatColor = new Color(0.55f, 0.85f, 1f);
        private int sewFromPiece = -1, sewFromEdge = -1;
        private bool dragging;
        private bool changedDuringDrag;
        private string lastJson;
        private Vector2 sideScroll;
        private string status;
        private const float SideWidth = 290f;

        public static PatternCanvasWindow Open(DesignerSession session)
        {
            var window = GetWindow<PatternCanvasWindow>("Pattern Canvas");
            if (window.session != null) window.session.SelectedPieceChanged -= window.Repaint;
            window.session = session;
            if (session != null) session.SelectedPieceChanged += window.Repaint;
            window.syncedPiece = int.MinValue;
            window.minSize = new Vector2(720f, 420f);
            window.lastJson = session.Pattern != null ? JsonUtility.ToJson(session.Pattern) : null;
            window.FrameAll();
            window.Show();
            return window;
        }

        private ClothesPattern Pattern => session != null ? session.Pattern : null;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            wantsMouseMove = true;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (session != null) session.SelectedPieceChanged -= Repaint;
        }

        // The selected piece both this window and the Scene view last agreed on.
        private int syncedPiece = int.MinValue;

        /// <summary>
        /// Keeps the selected piece the same here and in the Scene view: whichever side chose a piece since
        /// the last sync wins (the Scene view, if both did).
        /// </summary>
        private void SyncSelection()
        {
            if (session == null || Pattern == null) return;
            if (session.PlacePiece != syncedPiece)
            {
                syncedPiece = session.PlacePiece;
                if (selectedPiece != syncedPiece)
                {
                    selectedPiece = syncedPiece >= 0 && syncedPiece < Pattern.pieces.Count ? syncedPiece : -1;
                    selectedPoint = -1; selectedEdge = -1; selectedHandle = -1; selectedDart = -1; selectedPleat = -1; selectedLine = -1;
                }
            }
            else if (selectedPiece != syncedPiece)
            {
                syncedPiece = selectedPiece;
                session.PlacePiece = selectedPiece;
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// Lets go of a designer that has closed. Editing its pattern here would rebuild a cloth with no stage
        /// to hold it, and the cloth would appear in the open scene.
        /// </summary>
        private void ReleaseClosedSession()
        {
            if (session == null || !session.IsDisposed) return;
            session.SelectedPieceChanged -= Repaint;
            session = null;
            dragging = false;
            checks = null;
        }

        private void OnUndoRedo()
        {
            ReleaseClosedSession();
            if (Pattern == null) return;
            CommitIfChanged();
            Repaint();
        }

        /// <summary>Rebuilds the cloth when the pattern changed since the last rebuild.</summary>
        private void CommitIfChanged()
        {
            if (Pattern == null) return;
            // Linked mirrors follow their originals before the change is taken up.
            PatternEditing.UpdateLinkedMirrors(Pattern);
            string json = JsonUtility.ToJson(Pattern);
            if (json == lastJson) return;
            lastJson = json;
            ClampSelection();
            // A fabric change keeps the drape; a change of shape lays the pieces out again.
            session.SyncToData();
        }

        private void ClampSelection()
        {
            if (selectedPiece >= Pattern.pieces.Count) selectedPiece = Pattern.pieces.Count - 1;
            if (selectedTack >= Pattern.tacks.Count) selectedTack = -1;
            if (selectedPart >= Pattern.parts.Count) selectedPart = -1;
            if (selectedPiece < 0) { selectedPoint = -1; selectedEdge = -1; selectedDart = -1; selectedPleat = -1; selectedLine = -1; return; }
            if (selectedLine >= Pattern.pieces[selectedPiece].lines.Count) selectedLine = -1;
            int n = Pattern.pieces[selectedPiece].points.Count;
            if (selectedPoint >= n) selectedPoint = -1;
            if (selectedEdge >= n) selectedEdge = -1;
            if (selectedDart >= Pattern.pieces[selectedPiece].darts.Count) selectedDart = -1;
            if (selectedPleat >= Pattern.pieces[selectedPiece].pleats.Count) selectedPleat = -1;
        }

        private void Record(string what)
        {
            Undo.RecordObject(Pattern, what);
        }

        // ---- Coordinates ----------------------------------------------------------------------

        private Rect CanvasRect => new Rect(0f, 22f, Mathf.Max(100f, position.width - SideWidth), position.height - 22f);

        private Vector2 ToScreen(Vector2 cm)
        {
            Rect r = CanvasRect;
            return new Vector2(r.x + pan.x + cm.x * zoom, r.yMax + pan.y - cm.y * zoom);
        }

        private Vector2 ToCm(Vector2 screen)
        {
            Rect r = CanvasRect;
            return new Vector2((screen.x - r.x - pan.x) / zoom, (r.yMax + pan.y - screen.y) / zoom);
        }

        private Vector2 UvToScreen(Vector2 uv)
        {
            Rect r = CanvasRect;
            float size = Mathf.Min(r.width, r.height) - 40f;
            return new Vector2(r.x + 20f + uv.x * size, r.yMax - 20f - uv.y * size);
        }

        private void FrameAll()
        {
            if (Pattern == null || Pattern.pieces.Count == 0) return;
            Rect all = Pattern.pieces[0].Bounds();
            foreach (PatternPiece p in Pattern.pieces)
            {
                Rect b = p.Bounds();
                all = Rect.MinMaxRect(Mathf.Min(all.xMin, b.xMin), Mathf.Min(all.yMin, b.yMin), Mathf.Max(all.xMax, b.xMax), Mathf.Max(all.yMax, b.yMax));
            }
            Rect r = CanvasRect;
            zoom = Mathf.Clamp(Mathf.Min((r.width - 80f) / Mathf.Max(1f, all.width), (r.height - 80f) / Mathf.Max(1f, all.height)), 0.5f, 40f);
            pan = new Vector2(r.width * 0.5f - all.center.x * zoom, -(r.height * 0.5f - all.center.y * zoom));
        }

        // ---- GUI -----------------------------------------------------------------------------

        private void OnGUI()
        {
            ReleaseClosedSession();
            if (session == null || Pattern == null)
            {
                EditorGUILayout.HelpBox("Open the Clothes Designer in the Vertex Editor and choose a pattern, then press Pattern Canvas there.", MessageType.Info);
                return;
            }
            SyncSelection();
            EndTypingOnSelectionChange();
            if (HandleEditingKeys(Event.current)) { EndTypingOnSelectionChange(); Repaint(); }
            try
            {
                DrawCanvasGUI();
            }
            finally
            {
                // Also when a button ended the GUI early: a piece chosen here is selected in the Scene view.
                SyncSelection();
            }
        }

        /// <summary>The editor's window background, under the toolbar and the side panel.</summary>
        private static Color WindowBackground => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);

        private void DrawCanvasGUI()
        {
            Rect canvas = CanvasRect;
            EditorGUI.DrawRect(canvas, new Color(0.17f, 0.17f, 0.19f));
            // The canvas first, then the toolbar and the side panel over it: pieces panned or dragged past the canvas's
            // edge are not clipped, and drawn after them they showed through the side panel's controls.
            DrawCanvasContent(canvas);
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, canvas.y), WindowBackground);
            DrawToolbar();
            Rect side = new Rect(canvas.xMax, 22f, SideWidth, position.height - 22f);
            EditorGUI.DrawRect(side, WindowBackground);
            // A click on the canvas may have selected something else: a half-typed field of the last selection is dropped.
            EndTypingOnSelectionChange();
            DrawSidePanel(side);
            if (!string.IsNullOrEmpty(status)) GUI.Label(new Rect(canvas.x + 8f, canvas.yMax - 22f, canvas.width - 16f, 20f), status, EditorStyles.whiteMiniLabel);
        }

        private void DrawCanvasContent(Rect canvas)
        {
            if (mode == CanvasMode.UVs) DrawUvView();
            else
            {
                DrawReferenceImage(canvas);
                DrawGrid(canvas);
                DrawPieces();
                DrawSeams();
                DrawTacks();
                DrawTrims();
                DrawInProgress();
                DrawMoveAids();
                HandleCanvasInput(canvas);
            }
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            var newMode = (CanvasMode)GUILayout.Toolbar((int)mode, new[] { "Edit", "Sew", "Draw", "UVs" }, EditorStyles.toolbarButton, GUILayout.Width(230f));
            if (newMode != mode) { mode = newMode; CancelDrawing(); sewTargets.Clear(); sewFromLine = -1; }
            if (mode == CanvasMode.Draw)
            {
                var tool = (DrawTool)EditorGUILayout.Popup((int)drawTool, drawToolNames, EditorStyles.toolbarPopup, GUILayout.Width(110f));
                if (tool != drawTool) { drawTool = tool; CancelDrawing(); }
                if (drawTool == DrawTool.Tack) newTackIsButton = GUILayout.Toggle(newTackIsButton, new GUIContent("Button", "The tack is a button: the bake adds a button on the first piece."), EditorStyles.toolbarButton);
                if (drawTool == DrawTool.Cut)
                {
                    cutAndSew = GUILayout.Toggle(cutAndSew, new GUIContent("Sew Back", "Sew the two halves back together along the cut (Cut & Sew)."), EditorStyles.toolbarButton);
                    bool path = GUILayout.Toggle(KnifePath, new GUIContent("Path", "The knife follows the points you click (a princess seam, a curved yoke) instead of cutting straight across: double-click or Enter cuts, Backspace takes the last point back. An end inside the piece runs on to the outline."), EditorStyles.toolbarButton);
                    if (path != KnifePath) { KnifePath = path; CancelDrawing(); }
                    if (KnifePath)
                    {
                        bool smooth = GUILayout.Toggle(KnifeSmooth, new GUIContent("Smooth", "The cut curves through the clicked points; off, it runs straight from one to the next."), EditorStyles.toolbarButton);
                        if (smooth != KnifeSmooth) KnifeSmooth = smooth;
                    }
                }
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(new GUIContent("Add Piece", "A new rectangular piece to shape."), EditorStyles.toolbarButton))
            {
                Record("Add Piece");
                Rect at = new Rect(-20f, 0f, 40f, 40f);
                if (Pattern.pieces.Count > 0)
                {
                    Rect last = Pattern.pieces[Pattern.pieces.Count - 1].Bounds();
                    at.x = last.xMax + 10f;
                }
                Pattern.pieces.Add(PatternEditing.NewRectanglePiece("Piece " + (Pattern.pieces.Count + 1), at));
                selectedPiece = Pattern.pieces.Count - 1; selectedPoint = -1; selectedEdge = -1;
                CommitIfChanged();
            }
            using (new EditorGUI.DisabledScope(selectedPiece < 0))
            {
                if (GUILayout.Button(new GUIContent("Mirror", "A mirrored copy of the selected piece, beside it, linked: it follows every change to the original (unlink it in the piece panel to edit it on its own)."), EditorStyles.toolbarButton))
                {
                    Record("Mirror Piece");
                    PatternPiece src = Pattern.pieces[selectedPiece];
                    PatternPiece copy = PatternEditing.MirrorCopy(src, src.name + " Mirrored");
                    copy.mirrorLinked = true;
                    float shift = src.Bounds().width + 10f;
                    foreach (PatternPoint p in copy.points) p.position.x += shift;
                    Pattern.pieces.Add(copy);
                    selectedPiece = Pattern.pieces.Count - 1;
                    CommitIfChanged();
                }
                if (GUILayout.Button(new GUIContent("Duplicate", "Copies what is selected: a dart, a pleat, a line or a part, beside it (with Mirror X, mirrored across the piece's middle); with only a piece selected, the piece. Ctrl+D (Cmd+D)."), EditorStyles.toolbarButton))
                {
                    DuplicateSelection();
                }
                if (GUILayout.Button(new GUIContent("Delete Piece"), EditorStyles.toolbarButton))
                {
                    Record("Delete Piece");
                    PatternEditing.DeletePiece(Pattern, Pattern.pieces[selectedPiece]);
                    selectedPiece = -1; selectedPoint = -1; selectedEdge = -1; selectedDart = -1; selectedPleat = -1;
                    CommitIfChanged();
                }
            }
            GUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(selectedPiece < 0 || selectedEdge < 0))
            {
                if (GUILayout.Button(new GUIContent("Add Dart", "A dart opening on the selected edge: a V sewn shut to its point (bust, waist, shoulder, French or elbow dart). Select an edge first."), EditorStyles.toolbarButton))
                {
                    Record("Add Dart");
                    PatternPiece piece = Pattern.pieces[selectedPiece];
                    PatternEditing.AddEdgeDart(piece, selectedEdge, 0.5f);
                    SelectDart(piece.darts.Count - 1);
                    status = "Drag the dart's point to shape it, or drag the dart along its edge.";
                    CommitIfChanged();
                }
            }
            using (new EditorGUI.DisabledScope(selectedPiece < 0))
            {
                if (GUILayout.Button(new GUIContent("Add Diamond Dart", "A double-pointed (fish-eye) dart inside the selected piece, for shaping the waist of a longer bodice or dress. With Mirror X on, a mirrored pair, one each side of the middle."), EditorStyles.toolbarButton))
                {
                    Record("Add Diamond Dart");
                    PatternPiece piece = Pattern.pieces[selectedPiece];
                    // With Mirror X, waist darts come in pairs: one each side, halfway to the side seam.
                    PatternDart[] pair = MirrorX ? PatternEditing.AddDoubleDartPair(piece, piece.Bounds().center.x) : null;
                    if (pair != null)
                    {
                        SelectDart(piece.darts.Count - 2);
                        status = "Two diamond darts added, mirrored across the middle (Mirror X is on): drag either and its twin follows. Set the width in the side panel.";
                    }
                    else
                    {
                        PatternEditing.AddDoubleDart(piece);
                        SelectDart(piece.darts.Count - 1);
                        status = "Drag either point, or the dart itself. Set its width in the side panel.";
                    }
                    CommitIfChanged();
                }
            }
            using (new EditorGUI.DisabledScope(selectedPiece < 0 || selectedEdge < 0))
            {
                if (GUILayout.Button(new GUIContent("Add Pleat", "A pleat folded at the selected edge: knife, box or inverted. Select an edge first."), EditorStyles.toolbarButton))
                {
                    Record("Add Pleat");
                    PatternPiece piece = Pattern.pieces[selectedPiece];
                    PatternEditing.AddPleat(piece, selectedEdge, 0.5f);
                    SelectPleat(piece.pleats.Count - 1);
                    status = "Drag the pleat along its edge; set its type and depth in the side panel.";
                    CommitIfChanged();
                }
            }
            DrawMoveToolbar();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Frame All", "Fit every piece in the canvas."), EditorStyles.toolbarButton)) FrameAll();
            if (GUILayout.Button(new GUIContent("Save Pattern" + (session.PatternDirty ? " *" : "")), EditorStyles.toolbarButton)) session.SavePattern(false);
            if (GUILayout.Button(new GUIContent("Save As"), EditorStyles.toolbarButton)) session.SavePattern(true);
            GUILayout.EndHorizontal();
        }

        private void DrawGrid(Rect canvas)
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            float step = zoom >= 3f ? 1f : 5f;
            Vector2 min = ToCm(new Vector2(canvas.x, canvas.yMax)), max = ToCm(new Vector2(canvas.xMax, canvas.y));
            for (float x = Mathf.Floor(min.x / step) * step; x <= max.x; x += step)
            {
                bool major = Mathf.Abs(x % 10f) < 0.01f;
                if (!major && zoom < 3f) continue;
                Handles.color = major ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.04f);
                Vector2 a = ToScreen(new Vector2(x, min.y)), b = ToScreen(new Vector2(x, max.y));
                Handles.DrawLine(a, b);
            }
            for (float y = Mathf.Floor(min.y / step) * step; y <= max.y; y += step)
            {
                bool major = Mathf.Abs(y % 10f) < 0.01f;
                if (!major && zoom < 3f) continue;
                Handles.color = major ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.04f);
                Handles.DrawLine(ToScreen(new Vector2(min.x, y)), ToScreen(new Vector2(max.x, y)));
            }
            Handles.EndGUI();
        }

        private static readonly Color[] PieceColors =
        {
            new Color(0.95f, 0.55f, 0.35f), new Color(0.35f, 0.65f, 0.95f), new Color(0.45f, 0.85f, 0.45f),
            new Color(0.95f, 0.85f, 0.35f), new Color(0.75f, 0.5f, 0.95f), new Color(0.4f, 0.85f, 0.85f)
        };

        private void DrawPieces()
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            for (int p = 0; p < Pattern.pieces.Count; p++)
            {
                PatternPiece piece = Pattern.pieces[p];
                if (piece.points.Count < 2) continue;
                Color color = PieceColors[p % PieceColors.Length];
                // Fill.
                List<Vector2> outline = PatternGeometry.SampleOutline(piece, Mathf.Max(0.5f, 3f / zoom), null);
                var screen = new Vector3[outline.Count];
                for (int i = 0; i < outline.Count; i++) screen[i] = ToScreen(outline[i]);
                if (screen.Length >= 3) DrawFilled(screen, new Color(color.r, color.g, color.b, p == selectedPiece ? 0.22f : 0.12f));
                // Edges.
                for (int e = 0; e < piece.edges.Count; e++)
                {
                    var curve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
                    int steps = piece.edges[e].curved ? 32 : 1;
                    var line = new Vector3[steps + 1];
                    for (int i = 0; i <= steps; i++) line[i] = ToScreen(curve.At(i / (float)steps));
                    PatternEdge edge = piece.edges[e];
                    PatternSeam seam = Pattern.FindSeamOn(piece.id, edge.id);
                    bool selected = p == selectedPiece && e == selectedEdge;
                    bool sewFrom = p == sewFromPiece && e == sewFromEdge;
                    Color c = edge.opening ? new Color(1f, 0.65f, 0.2f) : seam != null ? SeamColor(seam) : Color.white;
                    if (selected || sewFrom) c = Color.yellow;
                    Handles.color = c;
                    if (edge.opening) Handles.DrawDottedLines(Pairs(line), 4f);
                    else Handles.DrawAAPolyLine(selected ? 5f : 3f, line);
                    if (edge.curved && selected)
                    {
                        PatternGeometry.EdgeControlPoints(piece, e, PieceScale.Identity, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3, out _);
                        Handles.color = new Color(0.6f, 0.9f, 1f);
                        Handles.DrawLine(ToScreen(p0), ToScreen(p1));
                        Handles.DrawLine(ToScreen(p3), ToScreen(p2));
                        DrawDot(ToScreen(p1), 5f, selectedHandle == 0 ? Color.yellow : new Color(0.6f, 0.9f, 1f));
                        DrawDot(ToScreen(p2), 5f, selectedHandle == 1 ? Color.yellow : new Color(0.6f, 0.9f, 1f));
                    }
                }
                DrawDartsAndPleats(p, piece);
                DrawLines(p, piece);
                // Points.
                for (int i = 0; i < piece.points.Count; i++)
                {
                    bool selected = p == selectedPiece && i == selectedPoint;
                    DrawDot(ToScreen(piece.points[i].position), selected ? 6f : 4f, selected ? Color.yellow : Color.white);
                }
                // Grain arrow and name.
                Rect b = piece.Bounds();
                float g = piece.grainAngle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(-Mathf.Sin(g), Mathf.Cos(g));
                Vector2 a0 = ToScreen(b.center - dir * b.height * 0.25f), a1 = ToScreen(b.center + dir * b.height * 0.25f);
                Handles.color = new Color(1f, 1f, 1f, 0.7f);
                Handles.DrawAAPolyLine(2f, a0, a1);
                Vector2 back = (a0 - a1).normalized * 8f, side = new Vector2(-back.y, back.x) * 0.6f;
                Handles.DrawAAPolyLine(2f, a1 + back + side, a1, a1 + back - side);
                GUI.Label(new Rect(ToScreen(b.center).x - 60f, ToScreen(b.center).y - 9f, 120f, 18f), piece.name, CenteredLabel);
            }
            Handles.EndGUI();
        }

        private void SelectDart(int index)
        {
            selectedDart = index; selectedPleat = -1; selectedPoint = -1; selectedHandle = -1; selectedLine = -1; selectedTack = -1;
        }

        private void SelectLine(int piece, int line)
        {
            selectedPiece = piece; selectedLine = line; selectedTack = -1; selectedDart = -1; selectedPleat = -1; selectedPoint = -1; selectedEdge = -1; selectedHandle = -1;
        }

        private static Color ColorOf(PatternLine line) => line.kind == PatternLineKind.Hole ? HoleColor : line.kind == PatternLineKind.Fold ? FoldColor : LineColor;

        /// <summary>Lines inside a piece: seam lines solid, folds dashed, holes outlined; sewn lines in their seam's colour.</summary>
        private void DrawLines(int p, PatternPiece piece)
        {
            for (int l = 0; l < piece.lines.Count; l++)
            {
                PatternLine line = piece.lines[l];
                if (line.points.Count < 1) continue;
                bool selected = p == selectedPiece && l == selectedLine;
                bool closed = LineGeometry.IsClosed(line);
                var pts = new Vector3[line.points.Count + (closed ? 1 : 0)];
                for (int i = 0; i < pts.Length; i++) pts[i] = ToScreen(line.points[i % line.points.Count]);
                List<PatternSeam> seams = Pattern.FindSeamsOn(piece.id, line.id);
                Color c = selected ? Color.yellow : seams.Count > 0 ? SeamColor(seams[0]) : ColorOf(line);
                Handles.color = c;
                if (line.kind == PatternLineKind.Fold && pts.Length > 1) Handles.DrawDottedLines(Pairs(pts), 5f);
                else if (pts.Length > 1) Handles.DrawAAPolyLine(selected ? 3.5f : 2.5f, pts);
                if (selected) for (int i = 0; i < line.points.Count; i++) DrawDot(ToScreen(line.points[i]), lineHandle == i ? 5.5f : 4f, Color.yellow);
                else if (line.points.Count > 0) DrawDot(ToScreen(line.points[0]), 2.5f, c);
            }
        }

        /// <summary>Tacks and buttons: a dashed thread between their two points.</summary>
        private void DrawTacks()
        {
            if (Event.current.type != EventType.Repaint || Pattern.tacks == null) return;
            Handles.BeginGUI();
            for (int t = 0; t < Pattern.tacks.Count; t++)
            {
                PatternTack tack = Pattern.tacks[t];
                if (Pattern.FindPiece(tack.pieceA) == null || Pattern.FindPiece(tack.pieceB) == null) continue;
                bool selected = t == selectedTack;
                Color c = selected ? Color.yellow : TackColor;
                Handles.color = c;
                Vector2 a = ToScreen(tack.a), b = ToScreen(tack.b);
                if ((a - b).sqrMagnitude > 4f) Handles.DrawDottedLine(a, b, 3f);
                DrawDot(a, tack.button ? 6f : 4f, c);
                DrawDot(b, 3.5f, c);
                if (tack.button) { Handles.color = new Color(0f, 0f, 0f, 0.6f); Handles.DrawWireDisc(a, Vector3.forward, 6f); }
            }
            for (int i = 0; i < Pattern.parts.Count; i++)
            {
                PatternPart part = Pattern.parts[i];
                if (Pattern.FindPiece(part.pieceId) == null) continue;
                Handles.color = i == selectedPart ? Color.yellow : PartColor;
                Vector2 at = ToScreen(part.position);
                float r = Mathf.Max(3f, part.sizeMm * 0.05f * zoom);
                if (part.shape == PartShape.Plate)
                {
                    float h = r * Mathf.Max(0.05f, part.aspect);
                    Quaternion turn = Quaternion.Euler(0f, 0f, -part.rotation);
                    Vector3[] box = { at + (Vector2)(turn * new Vector3(-r, -h)), at + (Vector2)(turn * new Vector3(r, -h)), at + (Vector2)(turn * new Vector3(r, h)), at + (Vector2)(turn * new Vector3(-r, h)), at + (Vector2)(turn * new Vector3(-r, -h)) };
                    Handles.DrawAAPolyLine(2f, box);
                }
                else Handles.DrawWireDisc(at, Vector3.forward, r);
            }
            Handles.EndGUI();
        }

        /// <summary>The line or cut being drawn, and the first end of a tack.</summary>
        private void DrawInProgress()
        {
            if (Event.current.type != EventType.Repaint || mode != CanvasMode.Draw) return;
            Handles.BeginGUI();
            Color c = drawTool == DrawTool.Hole ? HoleColor : drawTool == DrawTool.Fold ? FoldColor : drawTool == DrawTool.Cut ? Color.red : LineColor;
            // Where a click would land: on a point, onto the outline or lined up with others.
            Vector2 snapped = DrawSnap(Event.current.mousePosition, Event.current.shift, EditorGUI.actionKey, out int hoverPiece, out _, out _, out bool onPoint);
            Vector2 cursor = ToScreen(snapped);
            Handles.color = c;
            var cm = new List<Vector2>(drawPoints);
            if (cm.Count > 0) cm.Add(snapped);
            if (drawTool == DrawTool.Hole && cm.Count > 2) cm.Add(cm[0]);
            if (drawTool == DrawTool.Cut && KnifePath && KnifeSmooth && cm.Count > 2) cm = SmoothPath(cm, 8);
            var pts = new List<Vector3>();
            foreach (Vector2 q in cm) pts.Add(ToScreen(q));
            if (pts.Count > 1) Handles.DrawAAPolyLine(2f, pts.ToArray());
            foreach (Vector2 q in drawPoints) DrawDot(ToScreen(q), 3.5f, c);
            if (drawTool == DrawTool.Tack && tackFromPiece >= 0)
            {
                Handles.color = TackColor;
                Handles.DrawDottedLine(ToScreen(tackFrom), cursor, 3f);
                DrawDot(ToScreen(tackFrom), 4f, TackColor);
            }
            DrawSnapAids(cursor, onPoint, hoverPiece >= 0);
            Handles.EndGUI();
            Repaint();
        }

        private void CancelDrawing()
        {
            drawPoints.Clear();
            drawPiece = -1;
            tackFromPiece = -1;
        }

        private void DrawReferenceImage(Rect canvas)
        {
            Texture2D image = Pattern.referenceImage;
            if (image == null || Event.current.type != EventType.Repaint) return;
            float height = Mathf.Max(1f, Pattern.referenceHeightCm);
            float width = height * image.width / Mathf.Max(1f, image.height);
            Vector2 a = ToScreen(Pattern.referenceOrigin), b = ToScreen(Pattern.referenceOrigin + new Vector2(width, height));
            Rect r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            GUI.BeginClip(canvas);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Pattern.referenceOpacity));
            GUI.DrawTexture(new Rect(r.x - canvas.x, r.y - canvas.y, r.width, r.height), image, ScaleMode.StretchToFill, true);
            GUI.color = old;
            GUI.EndClip();
        }

        private void SelectPleat(int index)
        {
            selectedPleat = index; selectedDart = -1; selectedPoint = -1; selectedHandle = -1;
        }

        /// <summary>Dart legs and outlines, their points, and pleat fold lines (dashed where pressed into a fold).</summary>
        private void DrawDartsAndPleats(int p, PatternPiece piece)
        {
            for (int d = 0; d < piece.darts.Count; d++)
            {
                PatternDart dart = piece.darts[d];
                bool selected = p == selectedPiece && d == selectedDart;
                List<Vector2> polygon = DartGeometry.DartPolygon(piece, dart, Mathf.Max(0.3f, 3f / zoom));
                if (polygon == null) continue;
                var line = new Vector3[polygon.Count + (dart.kind == DartKind.Double ? 1 : 0)];
                for (int i = 0; i < line.Length; i++) line[i] = ToScreen(polygon[i % polygon.Count]);
                Handles.color = selected ? Color.yellow : DartColor;
                Handles.DrawAAPolyLine(selected ? 3f : 2f, line);
                DrawDot(ToScreen(dart.tip), selected && dartHandle == 0 ? 6f : 4.5f, selected ? Color.yellow : DartColor);
                if (dart.kind == DartKind.Double) DrawDot(ToScreen(dart.tip2), selected && dartHandle == 1 ? 6f : 4.5f, selected ? Color.yellow : DartColor);
            }
            List<Vector2> outline = piece.pleats.Count > 0 ? PatternGeometry.SampleOutline(piece, 0.5f, null) : null;
            for (int i = 0; i < piece.pleats.Count; i++)
            {
                PatternPleat pleat = piece.pleats[i];
                if (!DartGeometry.Pleat(piece, pleat, PieceScale.Identity, out DartGeometry.PleatShape shape)) continue;
                bool selected = p == selectedPiece && i == selectedPleat;
                var curve = new PatternGeometry.EdgeCurve(piece, shape.edge, PieceScale.Identity);
                var starts = new Vector2[shape.lineFractions.Length];
                for (int l = 0; l < starts.Length; l++)
                {
                    starts[l] = curve.At(shape.lineFractions[l]);
                    List<Vector2> rest = DartGeometry.PleatLine(pleat, shape, starts[l], outline, 1f);
                    Vector2 end = rest.Count > 0 ? rest[rest.Count - 1] : starts[l];
                    Handles.color = selected ? Color.yellow : PleatColor;
                    if (shape.crease[l]) Handles.DrawDottedLine(ToScreen(starts[l]), ToScreen(end), 4f);
                    else Handles.DrawLine(ToScreen(starts[l]), ToScreen(end));
                    // The stitched part, solid and thick.
                    float down = Mathf.Min(pleat.stitchedCm, (end - starts[l]).magnitude);
                    if (down > 0f) Handles.DrawAAPolyLine(3f, ToScreen(starts[l]), ToScreen(starts[l] + shape.inward * down));
                }
                // An arrow just outside the edge for each pair of lines brought together.
                foreach (Vector2Int pair in shape.stitchedLines)
                {
                    Vector2 a = ToScreen(starts[pair.x] - shape.inward * 1.2f), b = ToScreen(starts[pair.y] - shape.inward * 1.2f);
                    Handles.color = selected ? Color.yellow : PleatColor;
                    Handles.DrawAAPolyLine(2f, a, b);
                    Vector2 back = (a - b).normalized * 6f, side = new Vector2(-back.y, back.x) * 0.6f;
                    Handles.DrawAAPolyLine(2f, b + back + side, b, b + back - side);
                }
            }
        }

        private static GUIStyle centeredLabel;
        private static GUIStyle CenteredLabel
        {
            get
            {
                if (centeredLabel == null) centeredLabel = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter };
                return centeredLabel;
            }
        }

        private static Vector3[] Pairs(Vector3[] line)
        {
            var result = new Vector3[(line.Length - 1) * 2];
            for (int i = 0; i + 1 < line.Length; i++) { result[2 * i] = line[i]; result[2 * i + 1] = line[i + 1]; }
            return result;
        }

        /// <summary>
        /// Fills a simple polygon, concave or not, by ear clipping. The triangles are drawn in one plain
        /// (not anti-aliased) batch: anti-aliased triangles each blend a soft edge, and where many thin
        /// ones meet at a corner those edges overlap into dark streaks.
        /// </summary>
        private static Material fillMaterial;

        private static void DrawFilled(Vector3[] polygon, Color color)
        {
            var triangles = new List<Vector3>(polygon.Length * 3);
            var index = new List<int>(polygon.Length);
            for (int i = 0; i < polygon.Length; i++) index.Add(i);
            // Screen y points down, so a counter-clockwise outline in centimetres is clockwise here.
            double area = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                area += (double)a.x * b.y - (double)b.x * a.y;
            }
            float sign = area > 0 ? 1f : -1f;
            int guard = polygon.Length * polygon.Length;
            while (index.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int k = 0; k < index.Count; k++)
                {
                    Vector3 a = polygon[index[(k + index.Count - 1) % index.Count]], b = polygon[index[k]], c = polygon[index[(k + 1) % index.Count]];
                    float cross = ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * sign;
                    if (cross <= 0f) continue;
                    bool inside = false;
                    for (int m = 0; m < index.Count && !inside; m++)
                    {
                        if (m == k || m == (k + 1) % index.Count || m == (k + index.Count - 1) % index.Count) continue;
                        inside = InTriangle(polygon[index[m]], a, b, c, sign);
                    }
                    if (inside) continue;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    index.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // A crossing outline: leave the rest unfilled.
            }
            if (index.Count == 3) { triangles.Add(polygon[index[0]]); triangles.Add(polygon[index[1]]); triangles.Add(polygon[index[2]]); }
            if (triangles.Count == 0) return;
            if (fillMaterial == null)
            {
                fillMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                fillMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fillMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fillMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                fillMaterial.SetInt("_ZWrite", 0);
                fillMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            fillMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Handles.matrix);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            foreach (Vector3 v in triangles) GL.Vertex(v);
            GL.End();
            GL.PopMatrix();
        }

        private static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, float sign)
        {
            float d1 = ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) * sign;
            float d2 = ((c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x)) * sign;
            float d3 = ((a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x)) * sign;
            return d1 > 0f && d2 > 0f && d3 > 0f;
        }

        private static void DrawDot(Vector2 at, float radius, Color color)
        {
            Handles.color = color;
            Handles.DrawSolidDisc(at, Vector3.forward, radius);
        }

        private Color SeamColor(PatternSeam seam)
        {
            return PatternEditing.SeamColor(Pattern.seams.IndexOf(seam));
        }

        /// <summary>For each seam: lines joining the paired ends, so direction and pairing are visible.</summary>
        private void DrawSeams()
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            foreach (PatternSeam seam in Pattern.seams)
            {
                PatternPiece pa = Pattern.FindPiece(seam.a.pieceId), pb = Pattern.FindPiece(seam.b.pieceId);
                if (pa == null || pb == null) continue;
                PatternGeometry.EdgeCurve ca = LineGeometry.CurveOf(pa, seam.a.edgeId, PieceScale.Identity);
                PatternGeometry.EdgeCurve cb = LineGeometry.CurveOf(pb, seam.b.edgeId, PieceScale.Identity);
                if (ca == null || cb == null) continue;
                // A seam on part of an edge: that part, drawn thick in the seam's colour.
                if (seam.a.start > 0.001f || seam.a.end < 0.999f || seam.b.start > 0.001f || seam.b.end < 0.999f)
                {
                    Handles.color = SeamColor(seam);
                    foreach ((PatternGeometry.EdgeCurve curve, SeamSpan span) in new[] { (ca, seam.a), (cb, seam.b) })
                    {
                        var part = new Vector3[17];
                        for (int i = 0; i <= 16; i++) part[i] = ToScreen(curve.At(Mathf.Lerp(span.start, span.end, i / 16f)));
                        Handles.DrawAAPolyLine(5f, part);
                    }
                }
                Vector2 a0 = ca.At(seam.a.start), a1 = ca.At(seam.a.end);
                Vector2 b0 = cb.At(seam.reversed ? seam.b.end : seam.b.start), b1 = cb.At(seam.reversed ? seam.b.start : seam.b.end);
                Handles.color = new Color(SeamColor(seam).r, SeamColor(seam).g, SeamColor(seam).b, 0.45f);
                Handles.DrawDottedLine(ToScreen(a0), ToScreen(b0), 3f);
                Handles.DrawDottedLine(ToScreen(a1), ToScreen(b1), 3f);
                float mismatch = PatternEditing.SeamMismatchCm(Pattern, seam);
                Vector2 mid = (ToScreen(ca.At((seam.a.start + seam.a.end) * 0.5f)) + ToScreen(cb.At((seam.b.start + seam.b.end) * 0.5f))) * 0.5f;
                if (Mathf.Abs(mismatch) > 0.5f) GUI.Label(new Rect(mid.x - 40f, mid.y - 8f, 80f, 16f), "Δ " + mismatch.ToString("0.0") + " cm", EditorStyles.whiteMiniLabel);
            }
            Handles.EndGUI();
        }

        // ---- Input ---------------------------------------------------------------------------

        private void HandleCanvasInput(Rect canvas)
        {
            Event e = Event.current;
            if (!canvas.Contains(e.mousePosition) && !dragging) return;
            if (e.type == EventType.ScrollWheel)
            {
                Vector2 before = ToCm(e.mousePosition);
                zoom = Mathf.Clamp(zoom * (e.delta.y > 0f ? 0.9f : 1.1f), 0.3f, 60f);
                Vector2 after = ToScreen(before);
                pan += e.mousePosition - after;
                e.Use(); Repaint();
                return;
            }
            if (e.type == EventType.MouseDrag && (e.button == 2 || (e.button == 0 && e.alt)))
            {
                pan += e.delta; e.Use(); Repaint();
                return;
            }
            if (mode == CanvasMode.Draw && HandleDrawInput(e)) return;
            if (mode == CanvasMode.Sew && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && sewFromPiece >= 0 && sewTargets.Count > 0)
            {
                Record("Sew to Several");
                var others = new List<(PatternPiece, int)>();
                foreach (var t in sewTargets) others.Add((Pattern.pieces[t.piece], t.edge));
                List<PatternSeam> made = PatternEditing.SewOneToMany(Pattern, Pattern.pieces[sewFromPiece], sewFromEdge, others, true, out string problem);
                status = made.Count > 0 ? "Sewn to " + made.Count + " edges, sharing the first edge out by their lengths." : problem;
                sewFromPiece = -1; sewFromEdge = -1; sewTargets.Clear();
                CommitIfChanged();
                e.Use(); Repaint();
                return;
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Pick(e.mousePosition, out int piece, out int point, out int edge, out float fraction, out int handle);
                if (mode == CanvasMode.Sew)
                {
                    PickLineOrTack(e.mousePosition, out int linePiece, out int lineIndex, out _, out _, out _);
                    HandleSewClick(piece, edge, linePiece, lineIndex, e.shift);
                    e.Use(); Repaint();
                    return;
                }
                // Shift: the whole piece moves, whatever on it is nearer (an edge, a point, a tack, a line). At a far zoom
                // most of a small piece is within reach of an edge (measured on the Belted Dress's belt at Frame All: only
                // 8 to 48% of it picked the piece).
                if (e.shift)
                {
                    int under = PieceInside(e.mousePosition);
                    if (under < 0) under = piece;
                    if (under >= 0)
                    {
                        selectedPiece = under; selectedPart = -1; selectedTack = -1; selectedLine = -1; lineHandle = -1;
                        selectedDart = -1; selectedPleat = -1; dartHandle = -1; selectedPoint = -1; selectedEdge = -1; selectedHandle = -1;
                        StartDrag("Move Piece");
                        e.Use(); Repaint();
                        return;
                    }
                }
                int pickedPart = PickPart(e.mousePosition);
                if (pickedPart >= 0)
                {
                    selectedPart = pickedPart; selectedTack = -1; selectedLine = -1; selectedDart = -1; selectedPleat = -1; selectedPoint = -1; selectedEdge = -1;
                    StartDrag("Move Part");
                    e.Use(); Repaint();
                    return;
                }
                selectedPart = -1;
                if (PickLineOrTack(e.mousePosition, out int lp, out int li, out int lpoint, out int tack, out int tackEnd))
                {
                    if (tack >= 0)
                    {
                        selectedTack = tack; selectedLine = -1; selectedDart = -1; selectedPleat = -1; selectedPoint = -1; selectedEdge = -1;
                        lineHandle = tackEnd;
                        StartDrag("Move Tack");
                    }
                    else
                    {
                        SelectLine(lp, li);
                        lineHandle = lpoint;
                        StartDrag(lpoint >= 0 ? "Move Line Point" : "Move Line");
                    }
                    e.Use(); Repaint();
                    return;
                }
                selectedLine = -1; selectedTack = -1;
                if (PickDartOrPleat(e.mousePosition, out int dartPiece, out int dart, out int pleat, out int part))
                {
                    selectedPiece = dartPiece; selectedEdge = -1;
                    if (dart >= 0) SelectDart(dart); else SelectPleat(pleat);
                    dartHandle = part;
                    StartDrag(dart >= 0 ? "Move Dart" : "Move Pleat");
                    e.Use(); Repaint();
                    return;
                }
                selectedDart = -1; selectedPleat = -1; dartHandle = -1;
                if (handle >= 0) { selectedHandle = handle; StartDrag("Move Curve Handle"); }
                else if (point >= 0) { selectedPiece = piece; selectedPoint = point; selectedEdge = -1; selectedHandle = -1; StartDrag("Move Point"); }
                else if (edge >= 0)
                {
                    selectedPiece = piece; selectedEdge = edge; selectedPoint = -1; selectedHandle = -1;
                    if (e.clickCount == 2)
                    {
                        Record("Split Edge");
                        selectedPoint = PatternEditing.SplitEdge(Pattern, Pattern.pieces[piece], edge, fraction);
                        selectedEdge = -1;
                        CommitIfChanged();
                    }
                    else
                    {
                        // Dragged, the edge moves (both its ends); a click alone only selects it.
                        StartDrag("Move Edge");
                        status = "Drag to move the edge. Hold Shift as you press to move the whole piece.";
                    }
                }
                else if (piece >= 0) { selectedPiece = piece; selectedPoint = -1; selectedEdge = -1; selectedHandle = -1; StartDrag("Move Piece"); }
                else { selectedPiece = -1; selectedPoint = -1; selectedEdge = -1; selectedHandle = -1; }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && dragging && e.button == 0)
            {
                // Recorded before every change, not only at mouse-down: Unity compares a recorded object at the
                // end of the frame it was recorded in, and a drag's moves come in later frames, so a drag recorded
                // once was lost from Undo (or kept only as far as its first frame).
                Record(dragLabel);
                // The whole move from where the drag began, kept to an axis and snapped; applied to what was there then.
                Vector2 total = MoveSoFar(ToCm(e.mousePosition), e);
                RestoreDragSnapshot();
                ApplyMove(total, dragStartCm + total, e.shift);
                ApplyMirror();
                changedDuringDrag = true;
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                dragGuides.Clear();
                if (dragGroup >= 0) Undo.CollapseUndoOperations(dragGroup);
                Undo.IncrementCurrentGroup();
                if (changedDuringDrag) CommitIfChanged();
                NoteMoveEnded();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && (selectedPart >= 0 || selectedTack >= 0 || (selectedPiece >= 0 && selectedLine >= 0)))
            {
                DeleteSelectedLineOrTack();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedPiece >= 0 && (selectedDart >= 0 || selectedPleat >= 0))
            {
                DeleteSelectedDartOrPleat();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedPiece >= 0 && selectedPoint >= 0)
            {
                Record("Delete Point");
                if (PatternEditing.DeletePoint(Pattern, Pattern.pieces[selectedPiece], selectedPoint)) selectedPoint = -1;
                else status = "A piece needs at least three points.";
                CommitIfChanged();
                e.Use();
            }
        }

        /// <summary>The topmost piece whose outline contains the pointer, or -1.</summary>
        private int PieceInside(Vector2 mouse)
        {
            Vector2 cm = ToCm(mouse);
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                if (Pattern.pieces[p].points.Count >= 3 && PatternGeometry.PointInPolygon(PatternGeometry.SampleOutline(Pattern.pieces[p], 1f, null), cm)) return p;
            }
            return -1;
        }

        private int PickPart(Vector2 mouse)
        {
            for (int i = Pattern.parts.Count - 1; i >= 0; i--)
            {
                float r = Mathf.Max(6f, Pattern.parts[i].sizeMm * 0.05f * zoom);
                if ((ToScreen(Pattern.parts[i].position) - mouse).magnitude < r) return i;
            }
            return -1;
        }

        private void DeleteSelectedLineOrTack()
        {
            if (selectedPart >= 0 && selectedPart < Pattern.parts.Count) { Record("Delete Part"); Pattern.parts.RemoveAt(selectedPart); selectedPart = -1; CommitIfChanged(); return; }
            if (selectedTack >= 0 && selectedTack < Pattern.tacks.Count) { Record("Delete Tack"); Pattern.tacks.RemoveAt(selectedTack); }
            else if (selectedPiece >= 0 && selectedLine >= 0 && selectedLine < Pattern.pieces[selectedPiece].lines.Count)
            {
                Record("Delete Line");
                PatternEditing.DeleteLine(Pattern, Pattern.pieces[selectedPiece], Pattern.pieces[selectedPiece].lines[selectedLine]);
            }
            selectedTack = -1; selectedLine = -1; lineHandle = -1;
            CommitIfChanged();
        }

        /// <summary>A line's point, a line, or a tack's end under the pointer.</summary>
        private bool PickLineOrTack(Vector2 mouse, out int piece, out int line, out int point, out int tack, out int tackEnd)
        {
            piece = -1; line = -1; point = -1; tack = -1; tackEnd = -1;
            const float pickPixels = 7f;
            for (int t = Pattern.tacks.Count - 1; t >= 0; t--)
            {
                if ((ToScreen(Pattern.tacks[t].a) - mouse).magnitude < pickPixels) { tack = t; tackEnd = 0; return true; }
                if ((ToScreen(Pattern.tacks[t].b) - mouse).magnitude < pickPixels) { tack = t; tackEnd = 1; return true; }
            }
            Vector2 cm = ToCm(mouse);
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                PatternPiece pc = Pattern.pieces[p];
                for (int l = 0; l < pc.lines.Count; l++)
                {
                    PatternLine ln = pc.lines[l];
                    if (p == selectedPiece && l == selectedLine)
                    {
                        for (int i = 0; i < ln.points.Count; i++) if ((ToScreen(ln.points[i]) - mouse).magnitude < pickPixels) { piece = p; line = l; point = i; return true; }
                    }
                    if (ln.points.Count >= 2 && DistanceToLines(ln.points, cm, LineGeometry.IsClosed(ln)) * zoom < pickPixels) { piece = p; line = l; point = -2; return true; }
                }
            }
            return false;
        }

        /// <summary>Draw mode: lines, folds and holes point by point, tacks end to end, and cuts.</summary>
        private bool HandleDrawInput(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { CancelDrawing(); status = null; e.Use(); Repaint(); return true; }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { FinishDrawing(); e.Use(); Repaint(); return true; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Backspace && drawPoints.Count > 0)
            {
                drawPoints.RemoveAt(drawPoints.Count - 1);
                if (drawPoints.Count == 0) drawPiece = -1;
                e.Use(); Repaint();
                return true;
            }
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return false;
            // On a point, exactly on the outline (so a line can end there), or lined up with other points.
            Vector2 at = DrawSnap(e.mousePosition, e.shift, EditorGUI.actionKey, out int piece, out int edge, out float fraction, out _);
            if (piece < 0) { status = "Click on a piece."; e.Use(); return true; }
            if (drawTool == DrawTool.Part)
            {
                Record("Add Part");
                Pattern.parts.Add(new PatternPart { id = ClothesPattern.NewId(), name = "Part " + (Pattern.parts.Count + 1), pieceId = Pattern.pieces[piece].id, position = at, shape = PartShape.Button });
                selectedPart = Pattern.parts.Count - 1; selectedTack = -1; selectedLine = -1;
                status = "Part added: set its shape and size in the side panel. Drag it in Edit mode to move it.";
                CommitIfChanged();
                e.Use(); Repaint();
                return true;
            }
            if (drawTool == DrawTool.Tack)
            {
                if (tackFromPiece < 0)
                {
                    tackFromPiece = piece; tackFrom = at;
                    status = "Now click the point it is held to, on the same piece or another.";
                }
                else
                {
                    Record(newTackIsButton ? "Add Button" : "Add Tack");
                    PatternTack tack = PatternEditing.AddTack(Pattern, Pattern.pieces[tackFromPiece], tackFrom, Pattern.pieces[piece], at, newTackIsButton);
                    selectedTack = Pattern.tacks.IndexOf(tack);
                    tackFromPiece = -1;
                    status = newTackIsButton ? "Button added: it is sewn on the first point and buttoned through the second." : "Tack added.";
                    CommitIfChanged();
                }
                e.Use(); Repaint();
                return true;
            }
            if (drawPiece >= 0 && piece != drawPiece) { status = "A line stays on one piece. Press Enter to finish this one first."; e.Use(); return true; }
            drawPiece = piece;
            drawPoints.Add(at);
            if (drawTool == DrawTool.Cut && !KnifePath && drawPoints.Count == 2)
            {
                Record("Cut Piece");
                PatternPiece half = PatternEditing.CutPiece(Pattern, Pattern.pieces[drawPiece], drawPoints[0], drawPoints[1], cutAndSew, out string problem);
                status = half != null ? "Cut into '" + Pattern.pieces[drawPiece].name + "' and '" + half.name + "'" + (cutAndSew ? ", sewn back together along the cut." : ".") : problem;
                if (half == null) Undo.RevertAllInCurrentGroup();
                CancelDrawing();
                CommitIfChanged();
            }
            else if (e.clickCount == 2) FinishDrawing();
            else status = drawTool == DrawTool.Cut
                ? (KnifePath ? "Click the next point of the cut. Double-click or Enter cuts, Backspace takes the last point back, Escape cancels." : "Click where the cut ends; it runs straight across the whole piece.")
                : "Click the next point. Double-click or Enter finishes, Backspace takes the last point back, Escape cancels.";
            e.Use(); Repaint();
            return true;
        }

        private void FinishDrawing()
        {
            // A double-click adds the same point twice.
            for (int i = drawPoints.Count - 1; i > 0; i--) if ((drawPoints[i] - drawPoints[i - 1]).sqrMagnitude < 0.01f) drawPoints.RemoveAt(i);
            if (drawTool == DrawTool.Cut && KnifePath && drawPiece >= 0 && drawPiece < Pattern.pieces.Count)
            {
                if (drawPoints.Count < 2) { status = "Click at least two points for the cut."; CancelDrawing(); return; }
                Record("Cut Piece");
                PatternPiece target = Pattern.pieces[drawPiece];
                PatternPiece half = PatternEditing.CutPieceAlong(Pattern, target, drawPoints, KnifeSmooth, cutAndSew, out string problem);
                status = half != null ? "Cut into '" + target.name + "' and '" + half.name + "'" + (cutAndSew ? ", sewn back together along the cut." : ".") : problem;
                if (half == null) Undo.RevertAllInCurrentGroup();
                CancelDrawing();
                CommitIfChanged();
                return;
            }
            if (drawTool == DrawTool.Tack || drawTool == DrawTool.Cut || drawPiece < 0) { CancelDrawing(); return; }
            int least = drawTool == DrawTool.Hole ? 3 : 2;
            if (drawPoints.Count < least) { status = "Draw at least " + least + " points."; CancelDrawing(); return; }
            Record(drawTool == DrawTool.Hole ? "Add Hole" : drawTool == DrawTool.Fold ? "Add Fold Line" : "Add Line");
            PatternPiece piece = Pattern.pieces[drawPiece];
            var line = new PatternLine
            {
                id = ClothesPattern.NewId(),
                kind = drawTool == DrawTool.Hole ? PatternLineKind.Hole : drawTool == DrawTool.Fold ? PatternLineKind.Fold : PatternLineKind.Seam,
                closed = drawTool == DrawTool.Hole
            };
            // Holes are kept counter-clockwise, like outlines.
            if (line.closed && PatternGeometry.SignedArea(drawPoints) < 0f) drawPoints.Reverse();
            line.points.AddRange(drawPoints);
            piece.lines.Add(line);
            SelectLine(drawPiece, piece.lines.Count - 1);
            status = line.kind == PatternLineKind.Seam ? "Line added. In Sew mode, click an edge of another piece, then this line, to sew it on." : line.kind == PatternLineKind.Fold ? "Fold line added. Set its angle in the side panel." : "Hole added.";
            CancelDrawing();
            CommitIfChanged();
        }

        private void DeleteSelectedDartOrPleat()
        {
            PatternPiece piece = Pattern.pieces[selectedPiece];
            if (selectedDart >= 0 && selectedDart < piece.darts.Count) { Record("Delete Dart"); piece.darts.RemoveAt(selectedDart); }
            else if (selectedPleat >= 0 && selectedPleat < piece.pleats.Count) { Record("Delete Pleat"); piece.pleats.RemoveAt(selectedPleat); }
            selectedDart = -1; selectedPleat = -1; dartHandle = -1;
            CommitIfChanged();
        }

        /// <summary>A dart's point (0) or second point (1), or the dart or pleat itself (2), under the pointer.</summary>
        private bool PickDartOrPleat(Vector2 mouse, out int piece, out int dart, out int pleat, out int part)
        {
            piece = -1; dart = -1; pleat = -1; part = -1;
            const float pickPixels = 8f;
            Vector2 cm = ToCm(mouse);
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                PatternPiece pc = Pattern.pieces[p];
                for (int d = 0; d < pc.darts.Count; d++)
                {
                    if ((ToScreen(pc.darts[d].tip) - mouse).magnitude < pickPixels) { piece = p; dart = d; part = 0; return true; }
                    if (pc.darts[d].kind == DartKind.Double && (ToScreen(pc.darts[d].tip2) - mouse).magnitude < pickPixels) { piece = p; dart = d; part = 1; return true; }
                }
            }
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                PatternPiece pc = Pattern.pieces[p];
                for (int d = 0; d < pc.darts.Count; d++)
                {
                    List<Vector2> polygon = DartGeometry.DartPolygon(pc, pc.darts[d], 0.5f);
                    if (polygon == null) continue;
                    bool closed = pc.darts[d].kind == DartKind.Double;
                    if (DistanceToLines(polygon, cm, closed) * zoom < pickPixels || (closed && PatternGeometry.PointInPolygon(polygon, cm))) { piece = p; dart = d; part = 2; return true; }
                }
                List<Vector2> outline = pc.pleats.Count > 0 ? PatternGeometry.SampleOutline(pc, 0.5f, null) : null;
                for (int i = 0; i < pc.pleats.Count; i++)
                {
                    if (!DartGeometry.Pleat(pc, pc.pleats[i], PieceScale.Identity, out DartGeometry.PleatShape shape)) continue;
                    var curve = new PatternGeometry.EdgeCurve(pc, shape.edge, PieceScale.Identity);
                    foreach (float f in shape.lineFractions)
                    {
                        Vector2 start = curve.At(f);
                        var line = new List<Vector2> { start };
                        line.AddRange(DartGeometry.PleatLine(pc.pleats[i], shape, start, outline, 1f));
                        // Lines meet the edge, so only the part inside counts, or the edge could not be picked.
                        if (line.Count > 1 && (cm - start).magnitude * zoom > pickPixels && DistanceToLines(line, cm, false) * zoom < pickPixels * 0.75f) { piece = p; pleat = i; part = 2; return true; }
                    }
                }
            }
            return false;
        }

        private static float DistanceToLines(List<Vector2> points, Vector2 p, bool closed)
        {
            float best = float.MaxValue;
            int count = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = points[i], ab = points[(i + 1) % points.Count] - a;
                float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        private void DragDart(PatternPiece piece, PatternDart dart, Vector2 delta, Vector2 mouseCm)
        {
            if (dartHandle == 0) { dart.tip += delta; return; }
            if (dartHandle == 1) { dart.tip2 += delta; return; }
            if (dart.kind == DartKind.Double) { dart.tip += delta; dart.tip2 += delta; return; }
            // An edge dart slides along its edge, its point going with it.
            int edge = piece.IndexOfEdge(dart.edgeId);
            if (edge < 0) return;
            var curve = new PatternGeometry.EdgeCurve(piece, edge, PieceScale.Identity);
            float half = dart.widthCm * 0.5f / Mathf.Max(0.01f, curve.Length);
            float now = Mathf.Clamp(curve.Nearest(mouseCm, out _), half + 0.02f, 1f - half - 0.02f);
            dart.tip += curve.At(now) - curve.At(dart.position);
            dart.position = now;
        }

        private static void DragPleat(PatternPiece piece, PatternPleat pleat, Vector2 mouseCm)
        {
            int edge = piece.IndexOfEdge(pleat.edgeId);
            if (edge < 0) return;
            var curve = new PatternGeometry.EdgeCurve(piece, edge, PieceScale.Identity);
            pleat.position = Mathf.Clamp(curve.Nearest(mouseCm, out _), 0.02f, 0.98f);
        }

        private void StartDrag(string what)
        {
            // Each drag is one Undo step: its own group, every change during it recorded into that group.
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(what);
            dragGroup = Undo.GetCurrentGroup();
            dragLabel = what;
            Record(what);
            dragging = true;
            changedDuringDrag = false;
            BeginMoveSession(ToCm(Event.current.mousePosition));
        }

        private string dragLabel = "Move";
        private int dragGroup = -1;

        private void HandleSewClick(int piece, int edge, int linePiece, int line, bool shift)
        {
            // A line inside a piece: an edge of another piece is sewn onto it.
            if (edge < 0 && line >= 0)
            {
                if (sewFromPiece >= 0 && sewFromEdge >= 0)
                {
                    Record("Sew onto Line");
                    PatternSeam onLine = PatternEditing.SewToLine(Pattern, Pattern.pieces[sewFromPiece], sewFromEdge, Pattern.pieces[linePiece], Pattern.pieces[linePiece].lines[line], out string lineProblem);
                    status = onLine != null ? "Sewn onto the line. If the ends cross, select the edge and press Reverse." : lineProblem;
                    sewFromPiece = -1; sewFromEdge = -1; sewTargets.Clear();
                    CommitIfChanged();
                    return;
                }
                sewFromLine = line; sewFromPiece = linePiece; sewFromEdge = -1;
                status = "Now click the edge of another piece to sew onto this line.";
                return;
            }
            if (edge < 0) { sewFromPiece = -1; sewFromEdge = -1; sewFromLine = -1; sewTargets.Clear(); status = "Click an edge, then the edge it is sewn to. Shift-click more edges to share it out, then press Enter."; return; }
            if (sewFromPiece >= 0 && sewFromEdge < 0 && sewFromLine >= 0)
            {
                Record("Sew onto Line");
                PatternSeam onLine = PatternEditing.SewToLine(Pattern, Pattern.pieces[piece], edge, Pattern.pieces[sewFromPiece], Pattern.pieces[sewFromPiece].lines[sewFromLine], out string lineProblem);
                status = onLine != null ? "Sewn onto the line." : lineProblem;
                sewFromPiece = -1; sewFromLine = -1;
                CommitIfChanged();
                return;
            }
            if (sewFromPiece < 0)
            {
                sewFromPiece = piece; sewFromEdge = edge; sewFromLine = -1; sewTargets.Clear();
                status = "Now click the edge to sew it to (or shift-click several, then press Enter). Click empty canvas to cancel.";
                return;
            }
            if (shift)
            {
                if (!sewTargets.Contains((piece, edge))) sewTargets.Add((piece, edge));
                status = sewTargets.Count + " edges chosen. Shift-click more, or press Enter to sew the first edge to them in order.";
                return;
            }
            Record("Sew");
            PatternSeam seam = PatternEditing.Sew(Pattern, Pattern.pieces[sewFromPiece], sewFromEdge, Pattern.pieces[piece], edge, out string problem);
            status = seam != null ? "Sewn. Check the dotted lines join the right ends; use Reverse if they cross." : problem;
            sewFromPiece = -1; sewFromEdge = -1; sewTargets.Clear();
            selectedPiece = piece; selectedEdge = edge;
            CommitIfChanged();
        }

        /// <summary>
        /// How near the pointer must be to an edge, in pixels, to pick it: from outside its piece, and from inside it
        /// while editing. Zoomed out, a narrow piece is mostly within the full reach of an edge: at Frame All only 8 to 48%
        /// of the Belted Dress's belt picked the piece; with the short reach inside, 50 to 73% (the rest is its corner
        /// points and belt-loop tacks).
        /// </summary>
        public const float EdgePickPixels = 8f, EdgePickPixelsInside = 3f;

        /// <summary>
        /// The first edge of the piece within reach of a point (cm) at this zoom (pixels a cm): <see cref="EdgePickPixels"/>
        /// from outside the piece, <paramref name="insidePixels"/> from inside it. -1 if none.
        /// </summary>
        public static int EdgeInReach(PatternPiece piece, Vector2 cm, float zoom, float insidePixels, out float fraction)
        {
            fraction = 0f;
            if (piece == null || piece.points.Count < 2 || zoom <= 0f) return -1;
            bool within = piece.points.Count >= 3 && PatternGeometry.PointInPolygon(PatternGeometry.SampleOutline(piece, 1f, null), cm);
            float reach = (within ? insidePixels : EdgePickPixels) / zoom;
            for (int e = 0; e < piece.edges.Count; e++)
            {
                var curve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
                float f = curve.Nearest(cm, out float d);
                if (d < reach) { fraction = f; return e; }
            }
            return -1;
        }

        private void Pick(Vector2 mouse, out int piece, out int point, out int edge, out float fraction, out int handle)
        {
            piece = -1; point = -1; edge = -1; fraction = 0f; handle = -1;
            const float pickPixels = 8f;
            // Handles of the selected curved edge first.
            if (selectedPiece >= 0 && selectedEdge >= 0 && Pattern.pieces[selectedPiece].edges[selectedEdge].curved)
            {
                PatternGeometry.EdgeControlPoints(Pattern.pieces[selectedPiece], selectedEdge, PieceScale.Identity, out _, out Vector2 h1, out Vector2 h2, out _, out _);
                if ((ToScreen(h1) - mouse).magnitude < pickPixels) { piece = selectedPiece; handle = 0; return; }
                if ((ToScreen(h2) - mouse).magnitude < pickPixels) { piece = selectedPiece; handle = 1; return; }
            }
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                PatternPiece pc = Pattern.pieces[p];
                for (int i = 0; i < pc.points.Count; i++)
                {
                    if ((ToScreen(pc.points[i].position) - mouse).magnitude < pickPixels) { piece = p; point = i; return; }
                }
            }
            Vector2 cm = ToCm(mouse);
            // Editing, an edge is in reach only close to it from inside its piece, so pressing inside moves the piece;
            // sewing and drawing are about edges, so they keep the full reach both sides.
            float inside = mode == CanvasMode.Edit ? EdgePickPixelsInside : EdgePickPixels;
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                int e = EdgeInReach(Pattern.pieces[p], cm, zoom, inside, out float f);
                if (e >= 0) { piece = p; edge = e; fraction = f; return; }
            }
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                if (PatternGeometry.PointInPolygon(PatternGeometry.SampleOutline(Pattern.pieces[p], 1f, null), cm)) { piece = p; return; }
            }
        }

        // ---- UV view -------------------------------------------------------------------------

        private void DrawUvView()
        {
            Rect r = CanvasRect;
            float size = Mathf.Min(r.width, r.height) - 40f;
            var square = new Rect(r.x + 20f, r.yMax - 20f - size, size, size);
            EditorGUI.DrawRect(square, new Color(0.25f, 0.25f, 0.28f));
            if (Event.current.type == EventType.Repaint && session.Fit.Mesh != null)
            {
                Handles.BeginGUI();
                PatternMesh mesh = session.Fit.Mesh;
                for (int p = 0; p < Pattern.pieces.Count; p++)
                {
                    List<Vector2> outline = PatternGeometry.SampleOutline(Pattern.pieces[p], 0.5f, null);
                    var pts = new Vector3[outline.Count + 1];
                    for (int i = 0; i <= outline.Count; i++) pts[i] = UvToScreen(UvLayout.ToUV(Pattern, p, outline[i % outline.Count]));
                    Handles.color = PieceColors[p % PieceColors.Length];
                    Handles.DrawAAPolyLine(2f, pts);
                    Rect reserved = Pattern.pieces[p].uvIsland.reserved;
                    Handles.color = new Color(1f, 1f, 1f, 0.15f);
                    Vector2 r0 = UvToScreen(reserved.min), r1 = UvToScreen(reserved.max);
                    Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(r0.x, r1.y, r1.x, r0.y), Color.clear, new Color(1f, 1f, 1f, 0.2f));
                    Vector2 c = UvToScreen(reserved.center);
                    GUI.Label(new Rect(c.x - 60f, c.y - 9f, 120f, 18f), Pattern.pieces[p].name, CenteredLabel);
                }
                Handles.EndGUI();
            }
            var problems = session.Fit.Mesh != null ? UvLayout.FindProblems(Pattern, session.Fit.Mesh) : new List<string>();
            GUILayout.BeginArea(new Rect(square.xMax + 10f, r.y + 10f, Mathf.Max(160f, r.xMax - square.xMax - 20f), r.height - 20f));
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 80f;
            GUILayout.Label("UV Layout", EditorStyles.boldLabel);
            Pattern.uvLayout.bakeResolution = EditorGUILayout.IntPopup("Texture", Pattern.uvLayout.bakeResolution, new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
            Pattern.uvLayout.gutterPixels = EditorGUILayout.IntSlider("Gutter (px)", Pattern.uvLayout.gutterPixels, 0, 32);
            bool turn = EditorGUILayout.ToggleLeft(new GUIContent("Turn Pieces", "Let Repack turn pieces a quarter or half turn to fit them tighter. The grain stays along an axis of the texture."), Pattern.uvLayout.turnPieces);
            if (turn != Pattern.uvLayout.turnPieces) { Record("Turn Pieces"); Pattern.uvLayout.turnPieces = turn; }
            bool share = EditorGUILayout.ToggleLeft(new GUIContent("Mirrors Share Space", "A linked mirror (a left and right sleeve) uses its original's place in the texture, mirrored, so the pair costs the texels of one. Both then show the same texture: no different decal or wear on each side. Takes effect on Repack."), Pattern.uvLayout.shareMirrors);
            if (share != Pattern.uvLayout.shareMirrors) { Record("Mirrors Share Space"); Pattern.uvLayout.shareMirrors = share; }
            if (GUILayout.Button(new GUIContent("Repack UVs", "Lay the pieces out again: their outlines packed tightly, at one texel density (times each piece's Texture Detail). Painted textures made for the old layout will no longer line up.")))
            {
                if (EditorUtility.DisplayDialog("Repack UVs", "Lay the pieces out again in the texture? Any texture painted for the current layout will no longer line up with the pieces.", "Repack", "Cancel"))
                {
                    Record("Repack UVs");
                    float density = UvLayout.Repack(Pattern, out float coverage);
                    status = "Packed at " + density.ToString("0.0") + " texels per cm of the pattern as drawn; the cloth covers " + (coverage * 100f).ToString("0") + "% of the texture.";
                    CommitIfChanged();
                }
            }
            if (problems.Count == 0) GUILayout.Label("No problems: every piece is inside its place.", EditorStyles.wordWrappedMiniLabel);
            foreach (string problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            EditorGUIUtility.labelWidth = oldLabelWidth;
            GUILayout.EndArea();
        }

        // ---- Side panel ----------------------------------------------------------------------

        private void DrawSidePanel(Rect area)
        {
            GUILayout.BeginArea(area, EditorStyles.helpBox);
            sideScroll = GUILayout.BeginScrollView(sideScroll);
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.DelayedTextField("Pattern", Pattern.name);
            if (newName != Pattern.name) { Record("Rename Pattern"); Pattern.name = newName; }

            GUILayout.Space(4f);
            GUILayout.Label("Pieces", EditorStyles.boldLabel);
            for (int p = 0; p < Pattern.pieces.Count; p++)
            {
                bool on = GUILayout.Toggle(p == selectedPiece, Pattern.pieces[p].name, EditorStyles.radioButton);
                if (on && p != selectedPiece) { selectedPiece = p; selectedPoint = -1; selectedEdge = -1; selectedDart = -1; selectedPleat = -1; }
            }

            if (selectedTack >= 0 && selectedTack < Pattern.tacks.Count) DrawTackInspector(Pattern.tacks[selectedTack]);
            if (selectedPart >= 0 && selectedPart < Pattern.parts.Count) DrawPartInspector(Pattern.parts[selectedPart]);
            if (selectedPiece >= 0 && selectedPiece < Pattern.pieces.Count)
            {
                if (selectedLine >= 0 && selectedLine < Pattern.pieces[selectedPiece].lines.Count) DrawLineInspector(Pattern.pieces[selectedPiece], Pattern.pieces[selectedPiece].lines[selectedLine]);
                DrawPieceInspector(Pattern.pieces[selectedPiece]);
                DrawDartsAndPleatsInspector(Pattern.pieces[selectedPiece]);
                DrawTrimsInspector(Pattern.pieces[selectedPiece]);
            }

            GUILayout.Space(6f);
            DrawDimensionEditor();
            GUILayout.Space(6f);
            DrawReferenceFields();

            GUILayout.Space(6f);
            GUILayout.Label("Checks", EditorStyles.boldLabel);
            List<string> problems = Checks();
            if (problems.Count == 0) GUILayout.Label("Ready to simulate.", EditorStyles.wordWrappedMiniLabel);
            foreach (string problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            if (EditorGUI.EndChangeCheck()) CommitIfChanged();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // The Checks list and the pattern it was made for. A full check takes tens of milliseconds on a
        // pattern with diamond darts, too slow to run for every GUI event.
        private List<string> checks;
        private string checksJson;

        /// <summary>
        /// The pattern's problems, worked out again only when the pattern has changed, at a Layout event (so
        /// Layout and Repaint draw the same list) and not while something is being dragged.
        /// </summary>
        private List<string> Checks()
        {
            if (checks != null && (dragging || Event.current.type != EventType.Layout)) return checks;
            string json = JsonUtility.ToJson(Pattern);
            if (checks != null && json == checksJson) return checks;
            checksJson = json;
            checks = Pattern.FindProblems();
            foreach (PatternSeam seam in Pattern.seams)
            {
                float mismatch = PatternEditing.SeamMismatchCm(Pattern, seam);
                if (Mathf.Abs(mismatch) > 1f) checks.Add("A seam's sides differ by " + Mathf.Abs(mismatch).ToString("0.0") + " cm; the longer side will gather.");
            }
            int unsewn = 0;
            int partly = 0;
            foreach (PatternPiece piece in Pattern.pieces)
            {
                foreach (PatternEdge e in piece.edges)
                {
                    if (e.opening) continue;
                    if (Pattern.FindSeamOn(piece.id, e.id) == null) unsewn++;
                    else if (Pattern.FreeSpan(piece.id, e.id, out float f0, out float f1) && f1 - f0 > 0.05f) partly++;
                }
            }
            if (unsewn > 0) checks.Add(unsewn + " edges are neither sewn nor marked as openings.");
            if (partly > 0) checks.Add(partly + " edges are sewn along only part of their length; the rest is left open.");
            return checks;
        }

        /// <summary>
        /// The piece's look: the clothes' own overlay, or one of the pattern's looks (a fabric's textures at their
        /// real size and a colour), edited here for every piece that uses it.
        /// </summary>
        private void DrawLookField(PatternPiece piece)
        {
            if (Pattern.looks == null) Pattern.looks = new List<PieceLook>();
            var names = new List<string> { "Clothes' Own Overlay" };
            int current = 0;
            for (int i = 0; i < Pattern.looks.Count; i++)
            {
                names.Add((i + 1) + ". " + (string.IsNullOrEmpty(Pattern.looks[i].name) ? "Look" : Pattern.looks[i].name));
                if (Pattern.looks[i].id == piece.lookId) current = i + 1;
            }
            names.Add("New Look...");
            int picked = EditorGUILayout.Popup(new GUIContent("Look", "What the piece is made of in the texture. A look is a fabric's textures, tiled at their real size along the grain, and a colour; the bake makes an overlay per look, masked to its pieces, so pieces can be different fabrics on one slot and material and be recoloured separately."), current, names.ToArray());
            if (picked != current)
            {
                Record("Look");
                if (picked == names.Count - 1)
                {
                    var created = new PieceLook { id = ClothesPattern.NewId(), name = "Fabric " + (Pattern.looks.Count + 1) };
                    Pattern.looks.Add(created);
                    piece.lookId = created.id;
                }
                else piece.lookId = picked == 0 ? null : Pattern.looks[picked - 1].id;
            }
            PieceLook look = Pattern.FindLook(piece.lookId);
            if (look == null) return;
            EditorGUI.indentLevel++;
            string lookName = EditorGUILayout.DelayedTextField("Name", look.name);
            if (lookName != look.name) { Record("Look Name"); look.name = lookName; }
            var albedo = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Colour Texture", "The fabric's colour, a tileable texture. None paints the look's colour alone."), look.albedo, typeof(Texture2D), false);
            if (albedo != look.albedo) { Record("Look Texture"); look.albedo = albedo; }
            var normal = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Normal Map", "The fabric's weave, a tileable tangent-space normal map. It is combined with the drape's baked folds."), look.normal, typeof(Texture2D), false);
            if (normal != look.normal) { Record("Look Normal Map"); look.normal = normal; }
            var mask = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Mask Texture", "For the material's third channel (specular, or metallic and smoothness), tileable. None leaves the clothes' own."), look.mask, typeof(Texture2D), false);
            if (mask != look.mask) { Record("Look Mask"); look.mask = mask; }
            Color color = EditorGUILayout.ColorField(new GUIContent("Colour", "Multiplied with the colour texture; characters can change it at run time."), look.color);
            if (color != look.color) { Record("Look Colour"); look.color = color; }
            string sharedName = EditorGUILayout.DelayedTextField(new GUIContent("Shared Colour", "A shared colour name for the recipe (for example \"Shirt2\"), so characters set this look's colour by name. Empty keeps the colour above."), look.sharedColorName ?? "");
            if (sharedName != (look.sharedColorName ?? "")) { Record("Look Shared Colour"); look.sharedColorName = sharedName; }
            float repeat = Mathf.Max(0.5f, EditorGUILayout.DelayedFloatField(new GUIContent("Repeat (cm)", "How much cloth one repeat of the textures covers: the fabric's real size."), look.repeatCm));
            if (!Mathf.Approximately(repeat, look.repeatCm)) { Record("Look Repeat"); look.repeatCm = repeat; }
            float rotation = EditorGUILayout.Slider(new GUIContent("Rotation", "The textures' turn against each piece's grain: 45 for fabric cut on the bias."), look.rotation, -180f, 180f);
            if (!Mathf.Approximately(rotation, look.rotation)) { Record("Look Rotation"); look.rotation = rotation; }
            if (look.normal != null)
            {
                float strength = EditorGUILayout.Slider(new GUIContent("Normal Strength"), look.normalStrength, 0f, 2f);
                if (!Mathf.Approximately(strength, look.normalStrength)) { Record("Look Normal Strength"); look.normalStrength = strength; }
            }
            int users = 0;
            foreach (PatternPiece p in Pattern.pieces) if (p.lookId == look.id) users++;
            EditorGUILayout.LabelField(" ", users == 1 ? "Used by this piece." : "Used by " + users + " pieces.", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button(new GUIContent("Use on All Pieces", "Give every piece this look.")))
            {
                Record("Look on All Pieces");
                foreach (PatternPiece p in Pattern.pieces) p.lookId = look.id;
            }
            if (GUILayout.Button(new GUIContent("Delete Look", "Remove the look; its pieces show the clothes' own overlay again.")))
            {
                Record("Delete Look");
                foreach (PatternPiece p in Pattern.pieces) if (p.lookId == look.id) p.lookId = null;
                Pattern.looks.Remove(look);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        private void DrawPieceInspector(PatternPiece piece)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Piece", EditorStyles.boldLabel);
            string name = EditorGUILayout.DelayedTextField("Name", piece.name);
            if (name != piece.name) { Record("Rename Piece"); piece.name = name; }
            float grain = EditorGUILayout.DelayedFloatField(new GUIContent("Grain (deg)", "The lengthwise direction of the cloth, from the piece's up."), piece.grainAngle);
            if (!Mathf.Approximately(grain, piece.grainAngle)) { Record("Grain"); piece.grainAngle = grain; }
            Rect b = piece.Bounds();
            EditorGUILayout.LabelField("Size", b.width.ToString("0.0") + " x " + b.height.ToString("0.0") + " cm");

            GUILayout.Space(4f);
            GUILayout.Label("Fabric", EditorStyles.boldLabel);
            ClothesDesignerWorkflow.FabricField(session, piece, "Details", "CanvasFabric");
            var interfacing = (InterfacingLevel)EditorGUILayout.EnumPopup(new GUIContent("Interfacing",
                "A stiffening layer fused to the piece. It resists folding across the piece's height (as drawn) but still curves round the neck or wrist. Firm: collar stands, cuffs, waistbands. Light: collar leaves, plackets."), piece.interfacing);
            if (interfacing != piece.interfacing) { Record("Interfacing"); piece.interfacing = interfacing; }
            float detail = EditorGUILayout.Slider(new GUIContent("Mesh Detail",
                "How much finer than the Mesh Level this piece is meshed. Small pieces (collars, cuffs, bands) need 2 to 3 to hold a curve or a point. A piece with pleats is meshed at least as finely as half its shallowest pleat's depth, whatever this says, so its folds hold."), PatternPiece.DetailOf(piece), 1f, 4f);
            detail = Mathf.Round(detail * 2f) / 2f;
            if (!Mathf.Approximately(detail, PatternPiece.DetailOf(piece))) { Record("Mesh Detail"); piece.meshDetail = detail; }
            float textureDetail = EditorGUILayout.Slider(new GUIContent("Texture Detail",
                "How many texels per centimetre this piece gets in the texture, against the others: less for pieces seldom seen (a collar stand, a waistband's inside), more for a printed front. Takes effect on Repack UVs."), piece.textureDetail <= 0f ? 1f : piece.textureDetail, 0.25f, 2f);
            textureDetail = Mathf.Round(textureDetail * 4f) / 4f;
            if (!Mathf.Approximately(textureDetail, piece.textureDetail)) { Record("Texture Detail"); piece.textureDetail = textureDetail; }
            DrawLookField(piece);
            int layer = EditorGUILayout.IntSlider(new GUIContent("Layer", "Where pieces cross, a higher layer is kept outside a lower one: a pocket over its panel, a jacket front over a shirt. Pieces sewn edge to edge are best on one layer."), piece.layer, 0, 5);
            if (layer != piece.layer) { Record("Layer"); piece.layer = layer; }
            float pressure = EditorGUILayout.Slider(new GUIContent("Pressure", "Pushes the piece out from the body, like padding or the air in a puffer jacket. It shows most on stretchy fabric or pieces cut with room to spare."), piece.pressure, 0f, 1f);
            if (!Mathf.Approximately(pressure, piece.pressure)) { Record("Pressure"); piece.pressure = pressure; }
            float thickness = EditorGUILayout.Slider(new GUIContent("Thickness (mm)", "How thick the piece is baked: a belt, a waistband, a strap. 0 bakes it as thin as the rest. The thickness is built outward, so the piece stands out from what it is sewn to; the drape is not changed."), piece.thicknessMm, 0f, 30f);
            thickness = Mathf.Round(thickness * 2f) / 2f;
            if (!Mathf.Approximately(thickness, piece.thicknessMm)) { Record("Thickness"); piece.thicknessMm = thickness; }
            PatternPiece original = string.IsNullOrEmpty(piece.mirrorOf) ? null : Pattern.FindPiece(piece.mirrorOf);
            if (original != null)
            {
                bool linked = EditorGUILayout.ToggleLeft(new GUIContent("Linked to '" + original.name + "'", "A linked mirror follows every change to the piece it was mirrored from. Unlink it to change it on its own."), piece.mirrorLinked);
                if (linked != piece.mirrorLinked) { Record(linked ? "Link Mirror" : "Unlink Mirror"); piece.mirrorLinked = linked; }
                if (piece.mirrorLinked) EditorGUILayout.HelpBox("Edit '" + original.name + "': this piece follows it. Changes made here are replaced.", MessageType.None);
            }
            DrawAttachFields(piece);
            DrawMergeButtons(piece);
            DrawPieceTools(piece);
            if (selectedPiece >= 0 && session.ClothReady)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Placement on the Body", EditorStyles.boldLabel);
                ClothesDesignerWorkflow.DrawPlacementFields(session, selectedPiece, false);
            }

            if (selectedEdge >= 0 && selectedEdge < piece.edges.Count)
            {
                PatternEdge edge = piece.edges[selectedEdge];
                GUILayout.Space(4f);
                GUILayout.Label("Edge" + (string.IsNullOrEmpty(edge.name) ? "" : ": " + edge.name), EditorStyles.boldLabel);
                float length = PatternGeometry.EdgeLength(piece, selectedEdge, PieceScale.Identity);
                if (!edge.curved)
                {
                    float newLength = EditorGUILayout.DelayedFloatField("Length (cm)", length);
                    if (Mathf.Abs(newLength - length) > 1e-3f) { Record("Edge Length"); PatternEditing.SetEdgeLength(piece, selectedEdge, newLength); }
                }
                else EditorGUILayout.LabelField("Length (cm)", length.ToString("0.0"));
                bool curved = EditorGUILayout.Toggle(new GUIContent("Curved", "Drag the two handles in the canvas to shape it."), edge.curved);
                if (curved != edge.curved) { Record("Curve"); PatternEditing.ToggleCurve(piece, selectedEdge); }
                List<PatternSeam> seams = Pattern.FindSeamsOn(piece.id, edge.id);
                using (new EditorGUI.DisabledScope(seams.Count > 0))
                {
                    bool opening = EditorGUILayout.Toggle(new GUIContent("Opening", "A neckline, armhole or hem: never sewn."), edge.opening);
                    if (opening != edge.opening) { Record("Opening"); edge.opening = opening; }
                }
                bool elastic = EditorGUILayout.Toggle(new GUIContent("Elastic", "Elastic sewn along the edge: it draws the edge in and stretches back, like a waistband, a cuff or a gathered hem."), edge.elastic);
                if (elastic != edge.elastic) { Record("Elastic"); edge.elastic = elastic; }
                if (edge.elastic)
                {
                    float percent = EditorGUILayout.Slider(new GUIContent("  Drawn In To (%)", "The edge's length at rest, as a share of its drawn length."), edge.elasticPercent, 30f, 100f);
                    if (!Mathf.Approximately(percent, edge.elasticPercent)) { Record("Elastic"); edge.elasticPercent = percent; }
                }
                foreach (PatternSeam seam in seams) DrawSeamFields(piece, edge, seam);
                if (!edge.opening && Pattern.FreeSpan(piece.id, edge.id, out _, out _))
                {
                    if (GUILayout.Button(new GUIContent(seams.Count > 0 ? "Sew the Rest to..." : "Sew to...", "Then click the edge to sew it to; shift-click several edges and press Enter to share this edge out between them.")))
                    {
                        mode = CanvasMode.Sew; sewFromPiece = selectedPiece; sewFromEdge = selectedEdge; sewTargets.Clear();
                        status = "Now click the edge to sew it to, or shift-click several and press Enter.";
                    }
                }
                if (GUILayout.Button(new GUIContent("Add Trim", "A zipper, stitching, piping or hem along this edge, a little inside it. Publish and Open in Overlay Painter (in the Publish window) lays it out there as an editable layer.")))
                {
                    AddTrim(piece, edge.id);
                }
                if (GUILayout.Button(new GUIContent("Merge Its Points", "Stitch Points: the edge's two points become one, at its middle. The edge goes, with anything sewn to it; the edges either side keep theirs.")))
                {
                    MergeSelectedEdgePoints(piece);
                }
            }
            else if (selectedPoint >= 0 && selectedPoint < piece.points.Count)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Point", EditorStyles.boldLabel);
                Vector2 p = piece.points[selectedPoint].position;
                Vector2 np = EditorGUILayout.Vector2Field("Position (cm)", p);
                if (np != p) { Record("Move Point"); piece.points[selectedPoint].position = np; }
                GUILayout.Label("Delete removes the point: its two edges become one, and what was sewn along them stays sewn. Double-click an edge to add one.", EditorStyles.wordWrappedMiniLabel);
            }
            else GUILayout.Label("Click a point or an edge to edit it. Double-click an edge to split it.", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>Sew this piece onto another like a patch pocket: lines on the target where its free edges lie.</summary>
        private void DrawAttachFields(PatternPiece piece)
        {
            var names = new List<string>();
            var indices = new List<int>();
            for (int p = 0; p < Pattern.pieces.Count; p++) if (Pattern.pieces[p] != piece) { names.Add(Pattern.pieces[p].name); indices.Add(p); }
            if (names.Count == 0) return;
            GUILayout.Space(4f);
            GUILayout.Label(new GUIContent("Sew onto Another Piece", "Like a patch pocket: every free edge (not an opening, not sewn) is sewn onto a line drawn on the other piece where it lies."), EditorStyles.boldLabel);
            int current = Mathf.Max(0, indices.IndexOf(attachTarget));
            int picked = EditorGUILayout.Popup("Onto", current, names.ToArray());
            if (indices[picked] != attachTarget) { attachTarget = indices[picked]; attachAt = Pattern.pieces[attachTarget].Bounds().center; }
            attachAt = EditorGUILayout.Vector2Field(new GUIContent("Middle At (cm)", "Where this piece's middle goes on the other piece, in its centimetres on the canvas."), attachAt);
            if (GUILayout.Button("Sew On"))
            {
                Record("Sew onto Piece");
                List<PatternSeam> made = PatternEditing.AttachPiece(Pattern, piece, Pattern.pieces[attachTarget], attachAt, out string problem);
                status = made.Count > 0 ? "Sewn onto '" + Pattern.pieces[attachTarget].name + "' along " + made.Count + " lines; laid out over it, a layer above." : problem;
                if (made.Count == 0) Undo.RevertAllInCurrentGroup();
                CommitIfChanged();
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>For each seam joining this piece edge to edge with another along both whole edges: Merge.</summary>
        private void DrawMergeButtons(PatternPiece piece)
        {
            foreach (PatternSeam seam in Pattern.seams.ToArray())
            {
                if (seam.a.pieceId != piece.id && seam.b.pieceId != piece.id) continue;
                if (seam.folded || !seam.reversed || seam.a.pieceId == seam.b.pieceId) continue;
                if (seam.a.start > 0.001f || seam.a.end < 0.999f || seam.b.start > 0.001f || seam.b.end < 0.999f) continue;
                PatternPiece other = Pattern.FindPiece(seam.a.pieceId == piece.id ? seam.b.pieceId : seam.a.pieceId);
                if (other == null || other.IndexOfEdge(seam.a.pieceId == piece.id ? seam.b.edgeId : seam.a.edgeId) < 0 || piece.IndexOfEdge(seam.a.pieceId == piece.id ? seam.a.edgeId : seam.b.edgeId) < 0) continue;
                PatternEdge mine = piece.edges[piece.IndexOfEdge(seam.a.pieceId == piece.id ? seam.a.edgeId : seam.b.edgeId)];
                string along = string.IsNullOrEmpty(mine.name) ? "edge " + (piece.edges.IndexOf(mine) + 1) : mine.name;
                if (GUILayout.Button(new GUIContent("Merge with '" + other.name + "' along " + along, "Join the two pieces into one along this seam. The seam goes; the rest comes with the merged piece.")))
                {
                    Record("Merge Pieces");
                    PatternSeam forMerge = seam;
                    if (seam.a.pieceId != piece.id) forMerge = new PatternSeam { id = seam.id, a = seam.b, b = seam.a, reversed = seam.reversed, folded = seam.folded };
                    if (forMerge != seam) { Pattern.seams[Pattern.seams.IndexOf(seam)] = forMerge; }
                    PatternPiece merged = PatternEditing.MergePieces(Pattern, forMerge, out string problem);
                    status = merged != null ? "Merged into '" + merged.name + "'." + (problem != null ? " " + problem : "") + " The UVs tab may ask to Repack UVs." : problem;
                    if (merged == null) Undo.RevertAllInCurrentGroup();
                    selectedPiece = merged != null ? Pattern.pieces.IndexOf(merged) : selectedPiece;
                    ClampSelection();
                    CommitIfChanged();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static readonly string[] topstitchNames = { "None", "Single", "Double" };

        /// <summary>One seam on the selected edge: what it joins, how much of each side, and its finish.</summary>
        private void DrawSeamFields(PatternPiece piece, PatternEdge edge, PatternSeam seam)
        {
            bool isA = seam.a.pieceId == piece.id && seam.a.edgeId == edge.id;
            SeamSpan mine = isA ? seam.a : seam.b, other = isA ? seam.b : seam.a;
            PatternPiece otherPiece = Pattern.FindPiece(other.pieceId);
            string otherName = "(missing)";
            if (otherPiece != null)
            {
                int otherEdge = otherPiece.IndexOfEdge(other.edgeId);
                PatternLine otherLine = otherPiece.FindLine(other.edgeId);
                otherName = otherPiece.name + ", " + (otherEdge >= 0 ? (string.IsNullOrEmpty(otherPiece.edges[otherEdge].name) ? "edge " + (otherEdge + 1) : otherPiece.edges[otherEdge].name)
                    : otherLine != null ? "line " + (string.IsNullOrEmpty(otherLine.name) ? (otherPiece.lines.IndexOf(otherLine) + 1).ToString() : otherLine.name) : "?");
            }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Sewn to", otherName);
            EditorGUILayout.LabelField("Lengths", PatternEditing.SpanLengthCm(piece, mine).ToString("0.0") + " / " + (otherPiece != null ? PatternEditing.SpanLengthCm(otherPiece, other).ToString("0.0") : "?") + " cm");
            Vector2 span = new Vector2(mine.start, mine.end) * 100f;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Along this edge (%)", "Which part of this edge the seam uses: from and to, as a share of its length. Another seam can use the rest."), GUILayout.Width(120f));
            float s0 = EditorGUILayout.DelayedFloatField(span.x), s1 = EditorGUILayout.DelayedFloatField(span.y);
            EditorGUILayout.EndHorizontal();
            if (!Mathf.Approximately(s0, span.x) || !Mathf.Approximately(s1, span.y))
            {
                Record("Seam Span");
                mine.start = Mathf.Clamp01(Mathf.Min(s0, s1 - 1f) / 100f); mine.end = Mathf.Clamp01(Mathf.Max(s1, s0 + 1f) / 100f);
                if (isA) seam.a = mine; else seam.b = mine;
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Reverse", "Swap which ends meet."))) { Record("Reverse Seam"); seam.reversed = !seam.reversed; }
            if (GUILayout.Button("Unsew")) { Record("Unsew"); Pattern.seams.Remove(seam); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); GUIUtility.ExitGUI(); }
            EditorGUILayout.EndHorizontal();
            bool folded = EditorGUILayout.ToggleLeft(new GUIContent("Folded (turned over)",
                "The second piece is turned over the first along this seam, like a collar's leaf over its stand. Draw it as it looks turned over, lay it out turned over a little farther out, and the simulation keeps it turned."), seam.folded);
            if (folded != seam.folded) { Record("Fold Seam"); seam.folded = folded; }
            bool sharpSeam = EditorGUILayout.ToggleLeft(new GUIContent("Sharp",
                "A crisp seam: the baked slot keeps a normal for each side of it (a welt, a panel line on leather) instead of one shared, smooth normal. The two sides stay at one position and move as one."), seam.sharp);
            if (sharpSeam != seam.sharp) { Record("Sharp Seam"); seam.sharp = sharpSeam; }
            var stitch = (TopstitchKind)EditorGUILayout.Popup(new GUIContent("Topstitch", "Stitching beside the seam, drawn into the clothes' texture when they are baked."), (int)seam.topstitch, topstitchNames);
            if (stitch != seam.topstitch) { Record("Topstitch"); seam.topstitch = stitch; }
            if (seam.topstitch != TopstitchKind.None)
            {
                float offset = EditorGUILayout.Slider("  From the Seam (mm)", seam.topstitchOffsetMm, 1f, 15f);
                if (!Mathf.Approximately(offset, seam.topstitchOffsetMm)) { Record("Topstitch"); seam.topstitchOffsetMm = offset; }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawLineInspector(PatternPiece piece, PatternLine line)
        {
            GUILayout.Space(4f);
            GUILayout.Label("Line inside the piece", EditorStyles.boldLabel);
            string name = EditorGUILayout.DelayedTextField("Name", line.name ?? "");
            if (name != (line.name ?? "")) { Record("Rename Line"); line.name = name; }
            var kind = (PatternLineKind)EditorGUILayout.EnumPopup(new GUIContent("Kind", "Seam: an edge of another piece is sewn onto it. Fold: the cloth creases along it. Hole: the shape is cut out."), line.kind);
            if (kind != line.kind) { Record("Line Kind"); line.kind = kind; if (kind == PatternLineKind.Hole) line.closed = true; }
            if (line.kind != PatternLineKind.Hole)
            {
                bool closed = EditorGUILayout.Toggle(new GUIContent("Closed", "The last point joins the first."), line.closed);
                if (closed != line.closed) { Record("Close Line"); line.closed = closed; }
            }
            if (line.kind == PatternLineKind.Fold)
            {
                float angle = EditorGUILayout.Slider(new GUIContent("Fold Angle", "How far the cloth is folded along the line: 0 flat, 90 a right angle, 170 folded back on itself (a pressed crease)."), line.foldAngle, 0f, 170f);
                if (!Mathf.Approximately(angle, line.foldAngle)) { Record("Fold Angle"); line.foldAngle = angle; }
                bool inward = EditorGUILayout.Toggle(new GUIContent("Folds Inward", "The crease points in towards the body (a valley) instead of out (a ridge)."), line.foldInward);
                if (inward != line.foldInward) { Record("Fold Direction"); line.foldInward = inward; }
                bool sharpLine = EditorGUILayout.Toggle(new GUIContent("Sharp", "Bake the crease sharp: the slot is split along it with a normal for each side, so it shows as a crisp edge. The two sides stay one."), line.sharp);
                if (sharpLine != line.sharp) { Record("Sharp Fold"); line.sharp = sharpLine; }
            }
            EditorGUILayout.LabelField("Length", LineGeometry.Length(line).ToString("0.0") + " cm, " + line.points.Count + " points");
            foreach (PatternSeam seam in Pattern.FindSeamsOn(piece.id, line.id))
            {
                PatternPiece other = Pattern.FindPiece(seam.a.pieceId == piece.id && seam.a.edgeId == line.id ? seam.b.pieceId : seam.a.pieceId);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Sewn to " + (other != null ? other.name : "(missing)"), EditorStyles.miniLabel);
                if (GUILayout.Button("Reverse", GUILayout.Width(60f))) { Record("Reverse Seam"); seam.reversed = !seam.reversed; }
                if (GUILayout.Button("Unsew", GUILayout.Width(52f))) { Record("Unsew"); Pattern.seams.Remove(seam); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Label("Drag a point to move it, or the line to move it all. Delete removes it.", EditorStyles.wordWrappedMiniLabel);
            if (line.kind != PatternLineKind.Hole && GUILayout.Button(new GUIContent("Add Trim", "A zipper, stitching, piping or hem along this line. Publish and Open in Overlay Painter (in the Publish window) lays it out there as an editable layer.")))
            {
                AddTrim(piece, line.id);
            }
            if (!LineGeometry.IsClosed(line) && GUILayout.Button(new GUIContent("Cut Piece Along Line", "Cuts the piece in two along this line (a princess seam, a curved yoke) and sews the halves back together along it; the line goes. Its ends run on to the outline when short of it.")))
            {
                CutAlongSelectedLine(piece, line);
            }
            if (GUILayout.Button("Delete Line")) { DeleteSelectedLineOrTack(); GUIUtility.ExitGUI(); }
        }

        // ---- Trims ---------------------------------------------------------------------------

        private int selectedTrim = -1;

        private void AddTrim(PatternPiece piece, string pathId)
        {
            Record("Add Trim");
            if (Pattern.trims == null) Pattern.trims = new List<PatternTrim>();
            Pattern.trims.Add(GarmentTrims.Create(piece, pathId, 0f, 1f));
            selectedTrim = Pattern.trims.Count - 1;
            status = UMATrimPainting.Painter != null
                ? "Trim added. Set its construction, width and inset in the side panel; Publish and Open in Overlay Painter, in the Publish window, hands it to the Overlay Painter."
                : "Trim added. Install the Overlay Painter (Welcome to UMA > Plugins) to paint it.";
            CommitIfChanged();
        }

        /// <summary>The trims on a piece: pick one to edit its construction, width, inset and span.</summary>
        private void DrawTrimsInspector(PatternPiece piece)
        {
            if (Pattern.trims == null) return;
            var onPiece = new List<int>();
            for (int i = 0; i < Pattern.trims.Count; i++) if (Pattern.trims[i] != null && Pattern.trims[i].pieceId == piece.id) onPiece.Add(i);
            if (onPiece.Count == 0) return;
            GUILayout.Space(6f);
            GUILayout.Label(new GUIContent("Trims", "Zippers, stitching, piping and hems painted along edges and lines by the Overlay Painter (Publish and Open in Overlay Painter, in the Publish window). Texture, not geometry."), EditorStyles.boldLabel);
            foreach (int i in onPiece)
            {
                bool on = GUILayout.Toggle(i == selectedTrim, Pattern.trims[i].name, EditorStyles.radioButton);
                if (on && i != selectedTrim) { selectedTrim = i; Repaint(); }
            }
            if (selectedTrim < 0 || selectedTrim >= Pattern.trims.Count || Pattern.trims[selectedTrim].pieceId != piece.id) return;
            PatternTrim trim = Pattern.trims[selectedTrim];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string name = EditorGUILayout.DelayedTextField("Name", trim.name ?? "");
            if (name != trim.name) { Record("Rename Trim"); trim.name = name; }
            IUMATrimPainter painter = UMATrimPainting.Painter;
            if (painter != null)
            {
                IReadOnlyList<UMATrimConstruction> constructions = painter.Constructions;
                var labels = new string[constructions.Count];
                int current = -1;
                for (int c = 0; c < constructions.Count; c++) { labels[c] = constructions[c].menuPath; if (constructions[c].id == trim.constructionId) current = c; }
                int picked = EditorGUILayout.Popup(new GUIContent("Construction", "How the Overlay Painter draws it: one of its path generators. Fine-tune it there after publishing."), current, labels);
                if (picked != current && picked >= 0)
                {
                    Record("Trim Construction");
                    bool renamed = string.IsNullOrEmpty(trim.name) || trim.name == GarmentTrims.ShortName(trim.constructionName) || trim.name == "Trim";
                    trim.constructionId = constructions[picked].id;
                    trim.constructionName = constructions[picked].menuPath;
                    if (renamed) trim.name = GarmentTrims.ShortName(trim.constructionName);
                }
            }
            else
            {
                EditorGUILayout.LabelField("Construction", string.IsNullOrEmpty(trim.constructionName) ? "(none)" : trim.constructionName);
                EditorGUILayout.HelpBox("Install the Overlay Painter (Welcome to UMA > Plugins) to choose a construction and paint trims.", MessageType.Info);
            }
            float width = EditorGUILayout.Slider(new GUIContent("Width (cm)", "How wide the trim is on the cloth."), trim.widthCm, 0.05f, 10f);
            if (!Mathf.Approximately(width, trim.widthCm)) { Record("Trim Width"); trim.widthCm = width; }
            bool onEdge = piece.IndexOfEdge(trim.pathId) >= 0;
            if (onEdge)
            {
                float inset = EditorGUILayout.Slider(new GUIContent("Inset (cm)", "How far inside the piece the trim's middle runs from the edge. Half the width keeps it all on this piece; 0 centres it on the edge."), trim.insetCm, -5f, 10f);
                if (!Mathf.Approximately(inset, trim.insetCm)) { Record("Trim Inset"); trim.insetCm = inset; }
            }
            float start = trim.start, end = trim.end;
            EditorGUILayout.MinMaxSlider(new GUIContent("Span", "The part of the " + (onEdge ? "edge" : "line") + " the trim covers."), ref start, ref end, 0f, 1f);
            if (!Mathf.Approximately(start, trim.start) || !Mathf.Approximately(end, trim.end)) { Record("Trim Span"); trim.start = start; trim.end = end; }
            if (LineGeometry.CurveOf(piece, trim.pathId, PieceScale.Identity) == null)
                EditorGUILayout.HelpBox("The edge or line this trim followed no longer exists; it is left out when publishing.", MessageType.Warning);
            if (GUILayout.Button("Delete Trim"))
            {
                Record("Delete Trim");
                Pattern.trims.RemoveAt(selectedTrim);
                selectedTrim = -1;
                CommitIfChanged();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndVertical();
        }

        private static readonly Color TrimColor = new Color(1f, 0.55f, 0.85f);

        /// <summary>Each trim as a band of its width along its path, brighter when selected.</summary>
        private void DrawTrims()
        {
            if (Event.current.type != EventType.Repaint || Pattern.trims == null || Pattern.trims.Count == 0) return;
            Handles.BeginGUI();
            for (int i = 0; i < Pattern.trims.Count; i++)
            {
                List<Vector2> path = GarmentTrims.DrawnPath(Pattern, Pattern.trims[i], out _);
                if (path.Count < 2) continue;
                var line = new Vector3[path.Count];
                for (int k = 0; k < path.Count; k++) line[k] = ToScreen(path[k]);
                bool selected = i == selectedTrim;
                Handles.color = new Color(TrimColor.r, TrimColor.g, TrimColor.b, selected ? 0.55f : 0.3f);
                Handles.DrawAAPolyLine(Mathf.Max(2f, Pattern.trims[i].widthCm * zoom), line);
                Handles.color = selected ? Color.yellow : TrimColor;
                Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, line);
            }
            Handles.EndGUI();
        }

        private void DrawTackInspector(PatternTack tack)
        {
            GUILayout.Space(6f);
            PatternPiece a = Pattern.FindPiece(tack.pieceA), b = Pattern.FindPiece(tack.pieceB);
            GUILayout.Label(tack.button ? "Button" : "Tack", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Holds", (a != null ? a.name : "?") + " to " + (b != null ? b.name : "?"));
            bool button = EditorGUILayout.Toggle(new GUIContent("Button", "A button sewn on the first point and buttoned through the second. The bake adds the button."), tack.button);
            if (button != tack.button) { Record("Button"); tack.button = button; }
            if (tack.button)
            {
                float d = EditorGUILayout.Slider("Diameter (mm)", tack.buttonDiameterMm, 4f, 40f);
                if (!Mathf.Approximately(d, tack.buttonDiameterMm)) { Record("Button Size"); tack.buttonDiameterMm = d; }
            }
            if (GUILayout.Button(tack.button ? "Delete Button" : "Delete Tack")) { DeleteSelectedLineOrTack(); GUIUtility.ExitGUI(); }
        }

        private void DrawPartInspector(PatternPart part)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Part", EditorStyles.boldLabel);
            PatternPiece on = Pattern.FindPiece(part.pieceId);
            EditorGUILayout.LabelField("On", on != null ? on.name : "(missing)");
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("Name", part.name ?? "");
            var shape = (PartShape)EditorGUILayout.EnumPopup(new GUIContent("Shape", "Button: a round button. Plate: a flat rectangle, like a buckle plate or a label. Mesh: a model of your own (turn on Read/Write in its import settings); its z axis points out of the cloth."), part.shape);
            float size = EditorGUILayout.Slider(new GUIContent("Size (mm)", "Across: a button's diameter, a plate's width, a mesh's largest side."), part.sizeMm, 2f, 120f);
            float aspect = shape == PartShape.Plate ? EditorGUILayout.Slider(new GUIContent("Height / Width"), part.aspect, 0.05f, 3f) : part.aspect;
            float height = shape != PartShape.Mesh ? EditorGUILayout.Slider(new GUIContent("Stands Out (mm)"), part.heightMm, 0.2f, 20f) : part.heightMm;
            float rotation = EditorGUILayout.Slider(new GUIContent("Rotation"), part.rotation, -180f, 180f);
            Mesh mesh = shape == PartShape.Mesh ? (Mesh)EditorGUILayout.ObjectField("Mesh", part.mesh, typeof(Mesh), false) : part.mesh;
            if (EditorGUI.EndChangeCheck())
            {
                Record("Edit Part");
                part.name = name; part.shape = shape; part.sizeMm = size; part.aspect = aspect; part.heightMm = height; part.rotation = rotation; part.mesh = mesh;
            }
            GUILayout.Label("The bake adds it to the clothes, moving with the cloth, in the cloth's texture where it sits.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Delete Part")) { DeleteSelectedLineOrTack(); GUIUtility.ExitGUI(); }
        }

        private void DrawReferenceFields()
        {
            GUILayout.Label("Reference Image", EditorStyles.boldLabel);
            var image = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Image", "A picture shown behind the pieces to trace: a flat sketch, a photo of a pattern."), Pattern.referenceImage, typeof(Texture2D), false);
            if (image != Pattern.referenceImage) { Record("Reference Image"); Pattern.referenceImage = image; }
            if (Pattern.referenceImage == null) return;
            Vector2 origin = EditorGUILayout.Vector2Field("Lower Left (cm)", Pattern.referenceOrigin);
            float height = EditorGUILayout.FloatField("Height (cm)", Pattern.referenceHeightCm);
            float opacity = EditorGUILayout.Slider("Opacity", Pattern.referenceOpacity, 0.05f, 1f);
            if (origin != Pattern.referenceOrigin || !Mathf.Approximately(height, Pattern.referenceHeightCm) || !Mathf.Approximately(opacity, Pattern.referenceOpacity))
            {
                Record("Reference Image");
                Pattern.referenceOrigin = origin; Pattern.referenceHeightCm = Mathf.Max(1f, height); Pattern.referenceOpacity = opacity;
            }
        }

        private static readonly string[] PleatKindNames = { "Knife (fold faces right)", "Knife (fold faces left)", "Box", "Inverted" };

        private void DrawDartsAndPleatsInspector(PatternPiece piece)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Darts and Pleats", EditorStyles.boldLabel);
            if (piece.darts.Count == 0 && piece.pleats.Count == 0)
            {
                GUILayout.Label("Select an edge and press Add Dart or Add Pleat, or press Add Diamond Dart for a dart inside the piece.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            for (int d = 0; d < piece.darts.Count; d++)
            {
                string label = (piece.darts[d].kind == DartKind.Edge ? "Dart " : "Diamond dart ") + (d + 1);
                if (GUILayout.Toggle(d == selectedDart, label, EditorStyles.radioButton) && d != selectedDart) SelectDart(d);
            }
            for (int i = 0; i < piece.pleats.Count; i++)
            {
                if (GUILayout.Toggle(i == selectedPleat, "Pleat " + (i + 1) + " (" + PleatKindNames[(int)piece.pleats[i].kind] + ")", EditorStyles.radioButton) && i != selectedPleat) SelectPleat(i);
            }
            if (selectedDart >= 0 && selectedDart < piece.darts.Count) DrawDartFields(piece, piece.darts[selectedDart]);
            else if (selectedPleat >= 0 && selectedPleat < piece.pleats.Count) DrawPleatFields(piece, piece.pleats[selectedPleat]);
        }

        private void DrawDartFields(PatternPiece piece, PatternDart dart)
        {
            GUILayout.Space(4f);
            // With Mirror X, what is changed here is made to the dart's twin too.
            int twin = SidePanelDartTwin(piece, piece.darts.IndexOf(dart), out bool twinSwapped, out float twinAxis);
            string before = twin >= 0 ? JsonUtility.ToJson(dart) : null;
            if (twin >= 0) EditorGUILayout.LabelField("Mirror X: changes here are made to its twin, dart " + (twin + 1) + ", too.", EditorStyles.miniLabel);
            if (dart.kind == DartKind.Edge)
            {
                int edge = piece.IndexOfEdge(dart.edgeId);
                if (edge < 0) { EditorGUILayout.HelpBox("This dart's edge no longer exists.", MessageType.Warning); }
                else
                {
                    var curve = new PatternGeometry.EdgeCurve(piece, edge, PieceScale.Identity);
                    Vector2 mouth = curve.At(dart.position);
                    float along = EditorGUILayout.DelayedFloatField(new GUIContent("Along Edge (cm)", "Where the middle of the opening is, from the start of the edge."), dart.position * curve.Length);
                    if (!Mathf.Approximately(along, dart.position * curve.Length))
                    {
                        Record("Move Dart");
                        float now = Mathf.Clamp01(along / Mathf.Max(0.01f, curve.Length));
                        dart.tip += curve.At(now) - mouth;
                        dart.position = now;
                    }
                    float width = EditorGUILayout.DelayedFloatField(new GUIContent("Width (cm)", "The intake: how much fabric the dart takes out at the edge."), dart.widthCm);
                    if (!Mathf.Approximately(width, dart.widthCm)) { Record("Dart Width"); dart.widthCm = Mathf.Max(0.2f, width); }
                    float length = (dart.tip - mouth).magnitude;
                    float newLength = EditorGUILayout.DelayedFloatField(new GUIContent("Length (cm)", "From the edge to the point."), length);
                    if (!Mathf.Approximately(newLength, length) && length > 1e-3f) { Record("Dart Length"); dart.tip = mouth + (dart.tip - mouth) * (Mathf.Max(0.5f, newLength) / length); }
                    Vector2 tip = EditorGUILayout.Vector2Field("Point (cm)", dart.tip);
                    if (tip != dart.tip) { Record("Move Dart Point"); dart.tip = tip; }
                    float bow = EditorGUILayout.Slider(new GUIContent("Curve (cm)", "Bows the legs: positive takes out less near the point (belly and hip shaping), negative more. 0 is a straight dart."), dart.curveCm, -3f, DartGeometry.MaxInwardCurve(dart));
                    if (!Mathf.Approximately(bow, dart.curveCm)) { Record("Dart Curve"); dart.curveCm = bow; }
                }
            }
            else
            {
                Vector2 a = EditorGUILayout.Vector2Field("First Point (cm)", dart.tip);
                if (a != dart.tip) { Record("Move Dart Point"); dart.tip = a; }
                Vector2 b = EditorGUILayout.Vector2Field("Second Point (cm)", dart.tip2);
                if (b != dart.tip2) { Record("Move Dart Point"); dart.tip2 = b; }
                float width = EditorGUILayout.DelayedFloatField(new GUIContent("Width (cm)", "The intake at the widest point."), dart.widthCm);
                if (!Mathf.Approximately(width, dart.widthCm)) { Record("Dart Width"); dart.widthCm = Mathf.Max(0.2f, width); }
                float widest = EditorGUILayout.Slider(new GUIContent("Widest At", "Where it is widest, from the first point (0) to the second (1). Put it on the waist line."), dart.widestAt, 0.1f, 0.9f);
                if (!Mathf.Approximately(widest, dart.widestAt)) { Record("Dart Shape"); dart.widestAt = widest; }
                float round = EditorGUILayout.Slider(new GUIContent("Roundness", "0 a straight-sided diamond, 1 rounded sides (a fish-eye)."), dart.roundness, 0f, 1f);
                if (!Mathf.Approximately(round, dart.roundness)) { Record("Dart Shape"); dart.roundness = round; }
            }
            MirrorSidePanelDart(piece, dart, before, twin, twinSwapped, twinAxis);
            if (GUILayout.Button("Delete Dart")) { DeleteSelectedDartOrPleat(); GUIUtility.ExitGUI(); }
        }

        private void DrawPleatFields(PatternPiece piece, PatternPleat pleat)
        {
            GUILayout.Space(4f);
            int edge = piece.IndexOfEdge(pleat.edgeId);
            if (edge < 0) { EditorGUILayout.HelpBox("This pleat's edge no longer exists.", MessageType.Warning); }
            var kind = (PleatKind)EditorGUILayout.Popup(new GUIContent("Type", "Knife: one fold. Box: two folds turned away from each other, a flat panel on top. Inverted: two folds meeting on the outside."), (int)pleat.kind, PleatKindNames);
            if (kind != pleat.kind) { Record("Pleat Type"); pleat.kind = kind; }
            if (edge >= 0)
            {
                float length = PatternGeometry.EdgeLength(piece, edge, PieceScale.Identity);
                float along = EditorGUILayout.DelayedFloatField(new GUIContent("Along Edge (cm)", "Where the middle of the pleat is, from the start of the edge."), pleat.position * length);
                if (!Mathf.Approximately(along, pleat.position * length)) { Record("Move Pleat"); pleat.position = Mathf.Clamp01(along / Mathf.Max(0.01f, length)); }
            }
            float depth = EditorGUILayout.DelayedFloatField(new GUIContent("Depth (cm)", "How deep each fold goes. A knife pleat takes twice this from the edge; box and inverted pleats four times."), pleat.depthCm);
            if (!Mathf.Approximately(depth, pleat.depthCm)) { Record("Pleat Depth"); pleat.depthCm = Mathf.Max(0.2f, depth); }
            if (pleat.kind == PleatKind.Box)
            {
                float box = EditorGUILayout.DelayedFloatField(new GUIContent("Box Width (cm)", "The flat panel between the two folds."), pleat.boxWidthCm);
                if (!Mathf.Approximately(box, pleat.boxWidthCm)) { Record("Pleat Box Width"); pleat.boxWidthCm = Mathf.Max(0f, box); }
            }
            float stitched = EditorGUILayout.DelayedFloatField(new GUIContent("Stitched Down (cm)", "How far down from the edge the folds are sewn together. 0 holds them only at the edge."), pleat.stitchedCm);
            if (!Mathf.Approximately(stitched, pleat.stitchedCm)) { Record("Pleat Stitching"); pleat.stitchedCm = Mathf.Max(0f, stitched); }
            bool pressed = EditorGUILayout.Toggle(new GUIContent("Pressed", "Pressed folds keep a crease and fall in pleats; unpressed ones open out into soft folds below the stitching."), pleat.pressed);
            if (pressed != pleat.pressed) { Record("Pleat Pressed"); pleat.pressed = pressed; }
            using (new EditorGUI.DisabledScope(!pleat.pressed))
            {
                bool sharpFolds = EditorGUILayout.Toggle(new GUIContent("Sharp Folds", "Bake the pressed folds sharp: the slot is split along each fold with a normal for each side, so the pleats show crisp edges. The two sides stay one."), pleat.sharpFolds);
                if (sharpFolds != pleat.sharpFolds) { Record("Sharp Pleat Folds"); pleat.sharpFolds = sharpFolds; }
            }
            float fold = EditorGUILayout.DelayedFloatField(new GUIContent("Fold Length (cm)", "How far the fold lines run into the piece. 0 runs them across the whole piece."), pleat.lengthCm);
            if (!Mathf.Approximately(fold, pleat.lengthCm)) { Record("Pleat Length"); pleat.lengthCm = Mathf.Max(0f, fold); }
            if (DartGeometry.Pleat(piece, pleat, PieceScale.Identity, out DartGeometry.PleatShape shape))
            {
                GUILayout.Label("Takes up " + shape.intakeCm.ToString("0.0") + " cm of the edge.", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Delete Pleat")) { DeleteSelectedDartOrPleat(); GUIUtility.ExitGUI(); }
        }

        private void DrawDimensionEditor()
        {
            GUILayout.Label("Sizes", EditorStyles.boldLabel);
            GUILayout.Label("A size follows a body measurement plus an allowance, or a fixed length.", EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < Pattern.dimensions.Count; i++)
            {
                PatternDimension d = Pattern.dimensions[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                string name = EditorGUILayout.DelayedTextField(d.name);
                if (name != d.name) { Record("Rename Size"); d.name = name; }
                if (GUILayout.Button("x", GUILayout.Width(20f))) { Record("Remove Size"); Pattern.dimensions.RemoveAt(i); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break; }
                EditorGUILayout.EndHorizontal();
                var kind = (DimensionKind)EditorGUILayout.EnumPopup("Sizes", d.kind);
                var measurement = (BodyMeasurement)EditorGUILayout.EnumPopup("Follows", d.measurement);
                float allowance = EditorGUILayout.DelayedFloatField(measurement == BodyMeasurement.None ? "Length (cm)" : "Allowance (cm)", measurement == BodyMeasurement.None ? d.fixedCm : d.allowanceCm);
                int share = kind == DimensionKind.Width ? EditorGUILayout.IntField(new GUIContent("Shared by", "How many pieces go round the body at this line (2 for a front and a back)."), d.shareCount) : d.shareCount;
                float line = kind == DimensionKind.Width ? EditorGUILayout.DelayedFloatField(new GUIContent("At height (cm)", "Where on the pieces the width is measured."), d.lineY) : d.lineY;
                if (kind != d.kind || measurement != d.measurement || share != d.shareCount || !Mathf.Approximately(line, d.lineY)
                    || !Mathf.Approximately(allowance, measurement == BodyMeasurement.None ? d.fixedCm : d.allowanceCm))
                {
                    Record("Edit Size");
                    d.kind = kind; d.measurement = measurement; d.shareCount = Mathf.Max(1, share); d.lineY = line;
                    if (measurement == BodyMeasurement.None) d.fixedCm = allowance; else d.allowanceCm = allowance;
                }
                foreach (PatternPiece piece in Pattern.pieces)
                {
                    bool on = d.pieceIds.Contains(piece.id);
                    bool now = EditorGUILayout.ToggleLeft(piece.name, on);
                    if (now != on) { Record("Size Pieces"); if (now) d.pieceIds.Add(piece.id); else d.pieceIds.Remove(piece.id); }
                }
                foreach (EvaluatedDimension e in session.Fit.Dimensions)
                {
                    if (e.dimension.id == d.id) EditorGUILayout.LabelField("On this body", e.targetCm.ToString("0.0") + " cm (drawn " + e.drawnCm.ToString("0.0") + ")", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("Add Size"))
            {
                Record("Add Size");
                Pattern.dimensions.Add(new PatternDimension { id = ClothesPattern.NewId(), name = "Size " + (Pattern.dimensions.Count + 1), kind = DimensionKind.Width, measurement = BodyMeasurement.Chest, allowanceCm = 8f, shareCount = 2 });
            }
        }
    }
}
