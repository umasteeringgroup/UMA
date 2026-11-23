using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The 2D pattern canvas: draw the flat pieces in centimetres, curve and split edges, sew edges,
    /// mark openings, set the grain, mirror pieces, size them with dimensions, and see the UV layout.
    /// It edits the pattern open in the Clothes Designer; the cloth is rebuilt after each change.
    /// </summary>
    public sealed class PatternCanvasWindow : EditorWindow
    {
        private enum CanvasMode { Edit, Sew, UVs }

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
            window.session = session;
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
        }

        private void OnUndoRedo()
        {
            if (Pattern == null) return;
            CommitIfChanged();
            Repaint();
        }

        /// <summary>Rebuilds the cloth when the pattern changed since the last rebuild.</summary>
        private void CommitIfChanged()
        {
            if (Pattern == null) return;
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
            if (selectedPiece < 0) { selectedPoint = -1; selectedEdge = -1; selectedDart = -1; selectedPleat = -1; return; }
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
            if (session == null || Pattern == null)
            {
                EditorGUILayout.HelpBox("Open the Clothes Designer in the Vertex Editor and choose a pattern, then press Pattern Canvas there.", MessageType.Info);
                return;
            }
            DrawToolbar();
            Rect canvas = CanvasRect;
            EditorGUI.DrawRect(canvas, new Color(0.17f, 0.17f, 0.19f));
            GUI.BeginClip(canvas);
            GUI.EndClip();
            if (mode == CanvasMode.UVs) DrawUvView();
            else
            {
                DrawGrid(canvas);
                DrawPieces();
                DrawSeams();
                HandleCanvasInput(canvas);
            }
            DrawSidePanel(new Rect(canvas.xMax, 22f, SideWidth, position.height - 22f));
            if (!string.IsNullOrEmpty(status)) GUI.Label(new Rect(canvas.x + 8f, canvas.yMax - 22f, canvas.width - 16f, 20f), status, EditorStyles.whiteMiniLabel);
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            mode = (CanvasMode)GUILayout.Toolbar((int)mode, new[] { "Edit", "Sew", "UVs" }, EditorStyles.toolbarButton, GUILayout.Width(170f));
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
                if (GUILayout.Button(new GUIContent("Mirror", "A mirrored copy of the selected piece, beside it."), EditorStyles.toolbarButton))
                {
                    Record("Mirror Piece");
                    PatternPiece src = Pattern.pieces[selectedPiece];
                    PatternPiece copy = PatternEditing.MirrorCopy(src, src.name + " Mirrored");
                    float shift = src.Bounds().width + 10f;
                    foreach (PatternPoint p in copy.points) p.position.x += shift;
                    Pattern.pieces.Add(copy);
                    selectedPiece = Pattern.pieces.Count - 1;
                    CommitIfChanged();
                }
                if (GUILayout.Button(new GUIContent("Duplicate"), EditorStyles.toolbarButton))
                {
                    Record("Duplicate Piece");
                    PatternPiece src = Pattern.pieces[selectedPiece];
                    Pattern.pieces.Add(PatternEditing.Duplicate(src, src.name + " Copy", new Vector2(src.Bounds().width + 10f, 0f)));
                    selectedPiece = Pattern.pieces.Count - 1;
                    CommitIfChanged();
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
                if (GUILayout.Button(new GUIContent("Add Diamond Dart", "A double-pointed (fish-eye) dart inside the selected piece, for shaping the waist of a longer bodice or dress."), EditorStyles.toolbarButton))
                {
                    Record("Add Diamond Dart");
                    PatternPiece piece = Pattern.pieces[selectedPiece];
                    PatternEditing.AddDoubleDart(piece);
                    SelectDart(piece.darts.Count - 1);
                    status = "Drag either point, or the dart itself. Set its width in the side panel.";
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
            selectedDart = index; selectedPleat = -1; selectedPoint = -1; selectedHandle = -1;
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
                int ea = pa.IndexOfEdge(seam.a.edgeId), eb = pb.IndexOfEdge(seam.b.edgeId);
                if (ea < 0 || eb < 0) continue;
                var ca = new PatternGeometry.EdgeCurve(pa, ea, PieceScale.Identity);
                var cb = new PatternGeometry.EdgeCurve(pb, eb, PieceScale.Identity);
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
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Pick(e.mousePosition, out int piece, out int point, out int edge, out float fraction, out int handle);
                if (mode == CanvasMode.Sew)
                {
                    HandleSewClick(piece, edge);
                    e.Use(); Repaint();
                    return;
                }
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
                }
                else if (piece >= 0) { selectedPiece = piece; selectedPoint = -1; selectedEdge = -1; selectedHandle = -1; StartDrag("Move Piece"); }
                else { selectedPiece = -1; selectedPoint = -1; selectedEdge = -1; selectedHandle = -1; }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && dragging && e.button == 0)
            {
                Vector2 delta = new Vector2(e.delta.x, -e.delta.y) / zoom;
                PatternPiece piece = Pattern.pieces[selectedPiece];
                if (selectedDart >= 0 && selectedDart < piece.darts.Count) DragDart(piece, piece.darts[selectedDart], delta, ToCm(e.mousePosition));
                else if (selectedPleat >= 0 && selectedPleat < piece.pleats.Count) DragPleat(piece, piece.pleats[selectedPleat], ToCm(e.mousePosition));
                else if (selectedHandle >= 0 && selectedEdge >= 0)
                {
                    PatternEdge edge = piece.edges[selectedEdge];
                    if (selectedHandle == 0) edge.handleA += delta; else edge.handleB += delta;
                }
                else if (selectedPoint >= 0)
                {
                    Vector2 p = piece.points[selectedPoint].position + delta;
                    piece.points[selectedPoint].position = e.shift ? new Vector2(Mathf.Round(p.x), Mathf.Round(p.y)) : p;
                }
                else
                {
                    foreach (PatternPoint p in piece.points) p.position += delta;
                }
                changedDuringDrag = true;
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                if (changedDuringDrag) CommitIfChanged();
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
            Record(what);
            dragging = true;
            changedDuringDrag = false;
        }

        private void HandleSewClick(int piece, int edge)
        {
            if (edge < 0) { sewFromPiece = -1; sewFromEdge = -1; status = "Click an edge, then the edge it is sewn to."; return; }
            if (sewFromPiece < 0)
            {
                sewFromPiece = piece; sewFromEdge = edge;
                status = "Now click the edge to sew it to. Click empty canvas to cancel.";
                return;
            }
            Record("Sew");
            PatternSeam seam = PatternEditing.Sew(Pattern, Pattern.pieces[sewFromPiece], sewFromEdge, Pattern.pieces[piece], edge, out string problem);
            status = seam != null ? "Sewn. Check the dotted lines join the right ends; use Reverse if they cross." : problem;
            sewFromPiece = -1; sewFromEdge = -1;
            selectedPiece = piece; selectedEdge = edge;
            CommitIfChanged();
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
            for (int p = Pattern.pieces.Count - 1; p >= 0; p--)
            {
                PatternPiece pc = Pattern.pieces[p];
                for (int e = 0; e < pc.edges.Count; e++)
                {
                    var curve = new PatternGeometry.EdgeCurve(pc, e, PieceScale.Identity);
                    float f = curve.Nearest(cm, out float d);
                    if (d * zoom < pickPixels) { piece = p; edge = e; fraction = f; return; }
                }
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
            if (GUILayout.Button(new GUIContent("Repack UVs", "Lay the pieces out again. Painted textures made for the old layout will no longer line up.")))
            {
                if (EditorUtility.DisplayDialog("Repack UVs", "Lay the pieces out again in the texture? Any texture painted for the current layout will no longer line up with the pieces.", "Repack", "Cancel"))
                {
                    Record("Repack UVs");
                    float density = UvLayout.Repack(Pattern);
                    status = "Packed at " + density.ToString("0.0") + " texels per cm of the pattern as drawn.";
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

            if (selectedPiece >= 0 && selectedPiece < Pattern.pieces.Count)
            {
                DrawPieceInspector(Pattern.pieces[selectedPiece]);
                DrawDartsAndPleatsInspector(Pattern.pieces[selectedPiece]);
            }

            GUILayout.Space(6f);
            DrawDimensionEditor();

            GUILayout.Space(6f);
            GUILayout.Label("Checks", EditorStyles.boldLabel);
            List<string> problems = Pattern.FindProblems();
            foreach (PatternSeam seam in Pattern.seams)
            {
                float mismatch = PatternEditing.SeamMismatchCm(Pattern, seam);
                if (Mathf.Abs(mismatch) > 1f) problems.Add("A seam's sides differ by " + Mathf.Abs(mismatch).ToString("0.0") + " cm; the longer side will gather.");
            }
            int unsewn = 0;
            foreach (PatternPiece piece in Pattern.pieces) foreach (PatternEdge e in piece.edges) if (!e.opening && Pattern.FindSeamOn(piece.id, e.id) == null) unsewn++;
            if (unsewn > 0) problems.Add(unsewn + " edges are neither sewn nor marked as openings.");
            if (problems.Count == 0) GUILayout.Label("Ready to simulate.", EditorStyles.wordWrappedMiniLabel);
            foreach (string problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            if (EditorGUI.EndChangeCheck()) CommitIfChanged();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
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
                PatternSeam seam = Pattern.FindSeamOn(piece.id, edge.id);
                using (new EditorGUI.DisabledScope(seam != null))
                {
                    bool opening = EditorGUILayout.Toggle(new GUIContent("Opening", "A neckline, armhole or hem: never sewn."), edge.opening);
                    if (opening != edge.opening) { Record("Opening"); edge.opening = opening; }
                }
                if (seam != null)
                {
                    bool isA = seam.a.pieceId == piece.id && seam.a.edgeId == edge.id;
                    SeamSpan other = isA ? seam.b : seam.a;
                    PatternPiece otherPiece = Pattern.FindPiece(other.pieceId);
                    int otherEdge = otherPiece != null ? otherPiece.IndexOfEdge(other.edgeId) : -1;
                    string otherName = otherPiece != null && otherEdge >= 0 ? (string.IsNullOrEmpty(otherPiece.edges[otherEdge].name) ? "edge " + (otherEdge + 1) : otherPiece.edges[otherEdge].name) : null;
                    EditorGUILayout.LabelField("Sewn to", otherName != null ? otherPiece.name + ", " + otherName : "(missing)");
                    EditorGUILayout.LabelField("Lengths", PatternGeometry.EdgeLength(piece, selectedEdge, PieceScale.Identity).ToString("0.0") + " / " + (otherEdge >= 0 ? PatternGeometry.EdgeLength(otherPiece, otherEdge, PieceScale.Identity).ToString("0.0") : "?") + " cm");
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("Reverse", "Swap which ends meet."))) { Record("Reverse Seam"); seam.reversed = !seam.reversed; }
                    if (GUILayout.Button("Unsew")) { Record("Unsew"); Pattern.seams.Remove(seam); }
                    EditorGUILayout.EndHorizontal();
                }
                else if (!edge.opening)
                {
                    if (GUILayout.Button(new GUIContent("Sew to...", "Then click the edge to sew it to.")))
                    {
                        mode = CanvasMode.Sew; sewFromPiece = selectedPiece; sewFromEdge = selectedEdge;
                        status = "Now click the edge to sew it to.";
                    }
                }
            }
            else if (selectedPoint >= 0 && selectedPoint < piece.points.Count)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Point", EditorStyles.boldLabel);
                Vector2 p = piece.points[selectedPoint].position;
                Vector2 np = EditorGUILayout.Vector2Field("Position (cm)", p);
                if (np != p) { Record("Move Point"); piece.points[selectedPoint].position = np; }
                GUILayout.Label("Delete removes the point. Double-click an edge to add one.", EditorStyles.wordWrappedMiniLabel);
            }
            else GUILayout.Label("Click a point or an edge to edit it. Double-click an edge to split it.", EditorStyles.wordWrappedMiniLabel);
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
