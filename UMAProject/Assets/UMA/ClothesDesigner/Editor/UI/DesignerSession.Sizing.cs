using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>Which way a Size drag resizes a piece.</summary>
    public enum SizeAxis
    {
        /// <summary>The way the pointer moved further: across the piece or down it.</summary>
        Free = 0,
        /// <summary>Only its width (a side handle).</summary>
        Across = 1,
        /// <summary>Only its length (the hem handle).</summary>
        Down = 2
    }

    /// <summary>
    /// The Size tool: a piece made wider, narrower, longer or shorter on the body (<see cref="PieceSizing"/>). The
    /// selected piece has handles at its sides and its hem. While dragging, the simulation waits and the cloth is shown
    /// with the pieces moved to the new size by geometry alone (<see cref="SizePreview"/>), a few milliseconds a move.
    /// On release the pattern is changed, the cloth built again keeping its drape and simulated, and the view holds the
    /// preview until the simulation has caught up, then blends into it.
    /// </summary>
    public sealed partial class DesignerSession
    {
        /// <summary>What the last resize did, or why it did nothing.</summary>
        public string SizeMessage;
        /// <summary>Screen points a centimetre for a drag on a piece seen edge on, whose directions on screen are unknown.</summary>
        public const float SizeFallbackPointsPerCm = 10f;
        /// <summary>A drag's width or length is rounded to this (cm).</summary>
        public const float SizeStepCm = 0.5f;
        /// <summary>How far a handle's cube stands out from the cloth, in handle sizes.</summary>
        private const float SizeHandleReach = 0.35f;
        /// <summary>How close the pointer must be to a handle to take it (screen points).</summary>
        private const float SizeHandlePickPoints = 10f;

        private bool sizing;
        private int sizePiece = -1;
        private Vector2 sizeMouse0, sizeAcross, sizeAlong;
        private float sizeSide = 1f;
        private SizeAxis sizeAxis;
        private int sizeHandle = -1;
        private PieceScale sizeScale = PieceScale.Identity;
        private Vector2 sizeFrom, sizeTo, sizeShown;
        private string sizeWidthBy, sizeLengthBy;
        // The cloth as the drag began, with its directions along each piece, for the preview; Escape shows it again.
        private bool sizePreviewing;
        private Vector3[] preSizePositions, sizeAcross3, sizeUp3;
        private SizePreview.BodyPlanes sizeBody;
        private bool preSizeRunning;
        // After a release: the preview the view shows, on the new cloth, until the simulation catches up and it blends in.
        private Vector3[] blendFrom;
        private double blendHeldSince, blendStart = -1;
        /// <summary>How long the view blends from the Size preview into the simulated cloth (s).</summary>
        public const float SizeBlendSeconds = 0.5f;
        /// <summary>The longest the view holds the Size preview while the simulation eases in the new size (s).</summary>
        public const float SizeBlendHoldSeconds = 2f;
        // The selected piece's handles: its right side, its left side and its hem (a cloth point each, and a triangle
        // of that point's for the piece's directions there).
        private PatternMesh handleMesh;
        private int[] handleTriangles;
        private int handlePiece = -1;
        private readonly int[] handleVertex = { -1, -1, -1 };
        private readonly int[] handleTriangle = { -1, -1, -1 };
        private int hoverHandle = -1;

        /// <summary>
        /// A piece whose outline is resized takes the pieces that share a size with it along (both sleeves, a belt's two
        /// halves), so their seams still match. Off: only the piece (and its linked mirror).
        /// </summary>
        public static bool SizeMatchingPieces
        {
            get => EditorPrefs.GetBool("UMA.ClothesDesigner.SizeMatchingPieces", true);
            set => EditorPrefs.SetBool("UMA.ClothesDesigner.SizeMatchingPieces", value);
        }

        /// <summary>A Size drag is in progress.</summary>
        public bool Sizing => sizing;
        /// <summary>During a Size drag: the piece's width and length (cm, as cut) when it started, and what the drag gives.</summary>
        public Vector2 SizeDragFrom => sizeFrom;
        public Vector2 SizeDragTo => sizeTo;
        /// <summary>The cloth is shown at the drag's size (the preview), or blending from it into the simulated cloth.</summary>
        public bool SizePreviewShown => (sizing && sizePreviewing) || blendFrom != null;

        /// <summary>A piece's width and length as cut on this body (cm), from the built cloth's sizes when it is built.</summary>
        public Vector2 CutSizeOf(int pieceIndex)
        {
            if (Pattern == null) return Vector2.zero;
            if (Fit.Mesh != null && Fit.Mesh.scales != null && Fit.Mesh.scales.Length == Pattern.pieces.Count) return PieceSizing.CutSize(Pattern, Fit.Mesh.scales, pieceIndex);
            return PieceSizing.CutSize(Pattern, FitData, Body != null ? Body.Landmarks : null, pieceIndex);
        }

        /// <summary>The size (of the pattern's Sizes) that sets a piece's width or length on this body, or null.</summary>
        public PatternDimension SizeSetting(int pieceIndex, DimensionKind kind) =>
            PieceSizing.SizeFor(Pattern, FitData, Body != null ? Body.Landmarks : null, pieceIndex, kind);

        /// <summary>
        /// Makes a piece <paramref name="widthCm"/> wide and <paramref name="lengthCm"/> long as cut on this body (see
        /// <see cref="PieceSizing.Resize"/>), as one Undo step. Sewn cloth is built again keeping its drape, carried over
        /// through the pattern, and simulates, so it settles to the new size; cloth only laid out is laid out again. With
        /// <paramref name="shown"/> (the Size preview, on the cloth as it was), the view shows that, carried onto the new
        /// cloth, until the simulation has eased the new size in, then blends into the simulated cloth.
        /// </summary>
        public bool ResizePiece(int pieceIndex, float widthCm, float lengthCm, Vector3[] shown = null)
        {
            if (Pattern == null || FitData == null || pieceIndex < 0 || pieceIndex >= Pattern.pieces.Count)
            {
                SizeMessage = "Choose a piece first.";
                NotifyChanged();
                return false;
            }
            BodyLandmarks landmarks = Body != null ? Body.Landmarks : null;
            bool sewn = Fit.HasCloth && Fit.Sewn;
            PatternMesh oldMesh = Fit.Mesh;
            // How far the simulated cloth already shows the change (none, unless an earlier one is still easing in), and the
            // whole change, worked out first.
            Vector2[] reached = sewn ? Fit.PreviewScaleNow : null;
            Vector2[] whole = sewn ? PieceSizing.Preview(Pattern, FitData, landmarks, pieceIndex, widthCm, lengthCm, SizeMatchingPieces, out _) : null;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Size Piece");
            Undo.RecordObject(Pattern, "Size Piece");
            Undo.RecordObject(FitData, "Size Piece");
            PieceSizing.Result result = PieceSizing.Resize(Pattern, FitData, landmarks, pieceIndex, widthCm, lengthCm, SizeMatchingPieces);
            SizeMessage = result.message;
            if (result.changed)
            {
                EditorUtility.SetDirty(Pattern);
                EditorUtility.SetDirty(FitData);
                RebuildCloth(sewn, result.Before);
                PlacePiece = pieceIndex;
                if (Fit.HasCloth && sewn)
                {
                    // The cloth starts the size it was and eases to its new one, rather than jumping (which can blow
                    // loose cloth apart).
                    Fit.EaseFrom(EaseStart(reached, whole));
                    SetRunning(true);
                }
                if (shown != null && oldMesh != null && Fit.HasCloth && shown.Length == oldMesh.VertexCount)
                {
                    blendFrom = new PatternMeshSampler(oldMesh).Carry(shown, Fit.Mesh, result.Before);
                    blendHeldSince = EditorApplication.timeSinceStartup;
                    blendStart = -1;
                    Fit.Positions = (Vector3[])blendFrom.Clone();
                    UpdatePreview();
                }
            }
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
            NotifyChanged();
            return result.changed;
        }

        /// <summary>
        /// Each piece's scale, as a share of its new size, for cloth that shows <paramref name="reached"/> of a resize whose
        /// whole change is <paramref name="whole"/> (null reached: none of it yet).
        /// </summary>
        public static Vector2[] EaseStart(Vector2[] reached, Vector2[] whole)
        {
            if (whole == null) return null;
            var start = new Vector2[whole.Length];
            for (int i = 0; i < whole.Length; i++)
            {
                Vector2 r = reached != null && i < reached.Length ? reached[i] : Vector2.one, w = whole[i];
                float x = w.x > 1e-6f ? r.x / w.x : 1f, y = w.y > 1e-6f ? r.y / w.y : 1f;
                start[i] = new Vector2(Mathf.Abs(x - 1f) < 1e-5f ? 1f : x, Mathf.Abs(y - 1f) < 1e-5f ? 1f : y);
            }
            return start;
        }

        private void SizeSceneGUI(SceneView view, Event e, int id)
        {
            DrawPieceOutlines();
            FindSizeHandles();
            if (e.type == EventType.Repaint)
            {
                DrawSizeHandles(e.mousePosition);
                if (sizing) DrawSizeLabel(e.mousePosition);
            }
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseMove:
                    int hover = HandleUnderPointer(e.mousePosition);
                    if (hover != hoverHandle) { hoverHandle = hover; view.Repaint(); }
                    break;
                case EventType.MouseDown:
                    if (e.button != 0) break;
                    int handle = HandleUnderPointer(e.mousePosition);
                    if (handle >= 0)
                    {
                        int v = handleVertex[handle];
                        BeginSize(PlacePiece, Fit.Mesh.drawn[v], Fit.Positions[v], handleTriangle[handle], e.mousePosition, handle);
                        GUIUtility.hotControl = id;
                    }
                    else if (PatternPointAt(HandleUtility.GUIPointToWorldRay(e.mousePosition), out int piece, out Vector2 drawn, out Vector3 point, out int triangle))
                    {
                        PlacePiece = piece;
                        BeginSize(piece, drawn, point, triangle, e.mousePosition, -1);
                        GUIUtility.hotControl = id;
                    }
                    else PlacePiece = -1;
                    NotifyChanged();
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || !sizing) break;
                    sizeTo = SizeAfterDrag(sizeFrom, e.mousePosition - sizeMouse0, sizeAcross, sizeAlong, sizeSide, sizeScale, e.shift, sizeAxis);
                    if (sizeTo != sizeShown) ShowSizePreview();
                    view.Repaint();
                    NotifyChanged();
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    if (sizing) EndSize();
                    e.Use();
                    break;
                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape && sizing)
                    {
                        CancelSize();
                        GUIUtility.hotControl = 0;
                        view.Repaint();
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// Starts a Size drag on a piece, at a point of it (as drawn and in the scene) on a cloth triangle; from one of its
        /// handles (0 its right side, 1 its left, 2 its hem), or from anywhere on it (-1).
        /// </summary>
        private void BeginSize(int piece, Vector2 drawn, Vector3 point, int triangle, Vector2 mouse, int handle)
        {
            sizing = false;
            if (piece < 0 || piece >= Pattern.pieces.Count || triangle < 0 || previewTriangles == null) return;
            Vector3[] p = Fit.Positions;
            int ia = previewTriangles[triangle], ib = previewTriangles[triangle + 1], ic = previewTriangles[triangle + 2];
            sizeAcross = sizeAlong = Vector2.zero;
            if (ClothAxes(p[ia], p[ib], p[ic], Fit.Mesh.drawn[ia], Fit.Mesh.drawn[ib], Fit.Mesh.drawn[ic], out Vector3 x, out Vector3 y))
            {
                Vector2 o = HandleUtility.WorldToGUIPoint(point);
                sizeAcross = HandleUtility.WorldToGUIPoint(point + x) - o;
                sizeAlong = HandleUtility.WorldToGUIPoint(point + y) - o;
            }
            sizeHandle = handle;
            sizeAxis = handle == 2 ? SizeAxis.Down : handle >= 0 ? SizeAxis.Across : SizeAxis.Free;
            sizeSide = handle == 1 ? -1f : handle == 0 ? 1f : drawn.x >= Pattern.pieces[piece].Bounds().center.x ? 1f : -1f;
            sizeScale = Fit.Mesh.scales != null && piece < Fit.Mesh.scales.Length ? Fit.Mesh.scales[piece] : PieceScale.Identity;
            sizeFrom = sizeTo = sizeShown = CutSizeOf(piece);
            sizeMouse0 = mouse;
            sizePiece = piece;
            sizeWidthBy = SizeNote(piece, DimensionKind.Width);
            sizeLengthBy = SizeNote(piece, DimensionKind.Length);
            sizing = sizeFrom.x > 0f && sizeFrom.y > 0f;
            // The simulation waits while the drag shows the new size on the cloth as it is now.
            sizePreviewing = sizing && Fit.HasCloth;
            if (sizePreviewing)
            {
                blendFrom = null;
                preSizeRunning = Running;
                Running = false;
                preSizePositions = (Vector3[])Fit.SyncPositions().Clone();
                SizePreview.Axes(Fit.Mesh, preSizePositions, out sizeAcross3, out sizeUp3);
                sizeBody = Body != null ? new SizePreview.BodyPlanes(Body.Collider, preSizePositions) : null;
            }
        }

        /// <summary>Shows the cloth with the pieces the drag changes at their new size (<see cref="SizePreview"/>); nothing is simulated.</summary>
        private void ShowSizePreview()
        {
            sizeShown = sizeTo;
            if (!sizePreviewing || !Fit.HasCloth || preSizePositions == null || preSizePositions.Length != Fit.Mesh.VertexCount) return;
            BodyLandmarks landmarks = Body != null ? Body.Landmarks : null;
            Vector2[] factors = PieceSizing.Preview(Pattern, FitData, landmarks, sizePiece, sizeTo.x, sizeTo.y, SizeMatchingPieces, out _);
            Ray?[] rings = SizePreview.RingAxes(Fit.Mesh, Pattern, FitData, landmarks, factors);
            Fit.Positions = SizePreview.Shape(Fit.Mesh, preSizePositions, sizeAcross3, sizeUp3, factors, rings, sizeBody, FitData != null ? FitData.clearanceCm * 0.01f : 0.006f);
            UpdatePreview();
        }

        /// <summary>What else a change of a piece's width or length changes, in words, or null: a size it is set by, or the pieces matching it.</summary>
        public string SizeNote(int piece, DimensionKind kind)
        {
            string way = kind == DimensionKind.Width ? "Width" : "Length";
            PatternDimension size = SizeSetting(piece, kind);
            if (size != null)
            {
                var others = PieceSizing.OthersSizedBy(Pattern, size, piece);
                return way + " changes the size '" + size.name + "'" + (others.Count > 0 ? ": " + string.Join(", ", others) + " too" : "");
            }
            if (!SizeMatchingPieces) return null;
            var matching = PieceSizing.MatchingPieces(Pattern, FitData, Body != null ? Body.Landmarks : null, piece, kind);
            if (matching.Count == 0) return null;
            return way + " changes " + string.Join(", ", matching.ConvertAll(i => Pattern.pieces[i].name)) + " too (matching pieces)";
        }

        private void EndSize()
        {
            sizing = false;
            if (Mathf.Approximately(sizeTo.x, sizeFrom.x) && Mathf.Approximately(sizeTo.y, sizeFrom.y))
            {
                ShowClothAsBeforeSizing();
                NotifyChanged();
                return;
            }
            // The simulation still has the drape as the drag began: that is carried over, and the preview shown meanwhile.
            Vector3[] shown = sizePreviewing ? (Vector3[])Fit.Positions.Clone() : null;
            if (sizePreviewing)
            {
                Fit.Positions = (Vector3[])preSizePositions.Clone();
                UpdatePreview();
            }
            sizePreviewing = false;
            Running = preSizeRunning;
            ResizePiece(sizePiece, sizeTo.x, sizeTo.y, shown);
        }

        /// <summary>The view back to the cloth as it was when the drag began, and the simulation as it was.</summary>
        private void ShowClothAsBeforeSizing()
        {
            if (!sizePreviewing) return;
            sizePreviewing = false;
            if (Fit.HasCloth && preSizePositions != null && preSizePositions.Length == Fit.Mesh.VertexCount)
            {
                Fit.Positions = (Vector3[])preSizePositions.Clone();
                UpdatePreview();
            }
            Running = preSizeRunning;
        }

        /// <summary>
        /// After a release: the view holds the Size preview while the simulation eases the new size in (at most
        /// <see cref="SizeBlendHoldSeconds"/>), then blends from it into the simulated cloth over <see cref="SizeBlendSeconds"/>.
        /// Applied to the positions just read from the simulation, before they are drawn.
        /// </summary>
        private void BlendFromSizePreview()
        {
            if (blendFrom == null) return;
            Vector3[] p = Fit.Positions;
            if (p == null || p.Length != blendFrom.Length) { blendFrom = null; return; }
            double now = EditorApplication.timeSinceStartup;
            if (blendStart < 0)
            {
                if (Fit.PreviewEasing && now - blendHeldSince < SizeBlendHoldSeconds)
                {
                    System.Array.Copy(blendFrom, p, p.Length);
                    return;
                }
                blendStart = now;
            }
            float t = (float)((now - blendStart) / SizeBlendSeconds);
            if (t >= 1f) { blendFrom = null; return; }
            float s = t * t * (3f - 2f * t);
            for (int i = 0; i < p.Length; i++) p[i] = Vector3.LerpUnclamped(blendFrom[i], p[i], s);
        }

        /// <summary>Ends a Size drag without changing the pattern: the cloth goes back to where it was when the drag started.</summary>
        public void CancelSize()
        {
            if (!sizing) return;
            sizing = false;
            ShowClothAsBeforeSizing();
            NotifyChanged();
        }

        // ---- Handles --------------------------------------------------------------------------------

        /// <summary>The selected piece's handle points, found again when the piece or the cloth's mesh changes.</summary>
        private void FindSizeHandles()
        {
            if (handleMesh == Fit.Mesh && handlePiece == PlacePiece && handleTriangles == previewTriangles) return;
            handleMesh = Fit.Mesh;
            handlePiece = PlacePiece;
            handleTriangles = previewTriangles;
            for (int k = 0; k < 3; k++) handleVertex[k] = handleTriangle[k] = -1;
            hoverHandle = -1;
            PatternMesh mesh = Fit.Mesh;
            if (mesh == null || previewTriangles == null || mesh.pieceStart == null || PlacePiece < 0 || PlacePiece >= mesh.pieceStart.Length) return;
            int[] found = SizeHandleVertices(mesh, PlacePiece);
            for (int k = 0; k < 3; k++) handleVertex[k] = found[k];
            for (int t = 0; t + 2 < previewTriangles.Length; t += 3)
            {
                for (int c = 0; c < 3; c++)
                {
                    for (int k = 0; k < 3; k++) if (handleTriangle[k] < 0 && handleVertex[k] >= 0 && previewTriangles[t + c] == handleVertex[k]) handleTriangle[k] = t;
                }
            }
            for (int k = 0; k < 3; k++) if (handleTriangle[k] < 0) handleVertex[k] = -1;
        }

        /// <summary>
        /// Where a piece's handles go: the points of its cloth nearest the middle of its right side, of its left side and of
        /// its hem, as drawn. -1 for a piece with no cloth.
        /// </summary>
        public static int[] SizeHandleVertices(PatternMesh mesh, int piece)
        {
            int[] found = { -1, -1, -1 };
            if (mesh == null || mesh.pieceStart == null || piece < 0 || piece >= mesh.pieceStart.Length) return found;
            int start = mesh.pieceStart[piece], count = mesh.pieceCount[piece];
            if (count <= 0) return found;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int v = start; v < start + count; v++) { min = Vector2.Min(min, mesh.drawn[v]); max = Vector2.Max(max, mesh.drawn[v]); }
            Vector2 mid = (min + max) * 0.5f;
            Vector2[] at = { new Vector2(max.x, mid.y), new Vector2(min.x, mid.y), new Vector2(mid.x, min.y) };
            var best = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            for (int v = start; v < start + count; v++)
            {
                for (int k = 0; k < 3; k++)
                {
                    float d = (mesh.drawn[v] - at[k]).sqrMagnitude;
                    if (d < best[k]) { best[k] = d; found[k] = v; }
                }
            }
            return found;
        }

        /// <summary>The way a handle points on the cloth now: out of the piece's side, or down it from the hem. Zero when unknown.</summary>
        private Vector3 SizeHandleDirection(int k)
        {
            int t = handleTriangle[k];
            if (t < 0 || previewTriangles == null || t + 2 >= previewTriangles.Length) return Vector3.zero;
            Vector3[] p = Fit.Positions;
            int ia = previewTriangles[t], ib = previewTriangles[t + 1], ic = previewTriangles[t + 2];
            if (ia >= p.Length || ib >= p.Length || ic >= p.Length) return Vector3.zero;
            if (!ClothAxes(p[ia], p[ib], p[ic], Fit.Mesh.drawn[ia], Fit.Mesh.drawn[ib], Fit.Mesh.drawn[ic], out Vector3 x, out Vector3 y)) return Vector3.zero;
            Vector3 d = k == 0 ? x : k == 1 ? -x : -y;
            return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.zero;
        }

        private bool SizeHandleAt(int k, out Vector3 root, out Vector3 tip)
        {
            root = tip = Vector3.zero;
            int v = handleVertex[k];
            if (v < 0 || v >= Fit.Positions.Length) return false;
            Vector3 d = SizeHandleDirection(k);
            if (d == Vector3.zero) return false;
            root = Fit.Positions[v];
            tip = root + d * HandleUtility.GetHandleSize(root) * SizeHandleReach;
            return true;
        }

        /// <summary>The handle under the pointer (0 right side, 1 left side, 2 hem), or -1.</summary>
        private int HandleUnderPointer(Vector2 mouse)
        {
            if (handlePiece < 0 || handlePiece != PlacePiece) return -1;
            int nearest = -1;
            float best = SizeHandlePickPoints;
            for (int k = 0; k < 3; k++)
            {
                if (!SizeHandleAt(k, out Vector3 root, out Vector3 tip)) continue;
                Vector2 a = HandleUtility.WorldToGUIPoint(root), b = HandleUtility.WorldToGUIPoint(tip);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / ab.sqrMagnitude) : 1f;
                // The cube at the tip takes the pointer from a little further than the line.
                float d = Mathf.Min(Vector2.Distance(mouse, b) * 0.7f, Vector2.Distance(mouse, a + ab * t));
                if (d < best) { best = d; nearest = k; }
            }
            return nearest;
        }

        private void DrawSizeHandles(Vector2 mouse)
        {
            if (handlePiece < 0 || handlePiece != PlacePiece) return;
            UnityEngine.Rendering.CompareFunction oldZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int k = 0; k < 3; k++)
            {
                if (!SizeHandleAt(k, out Vector3 root, out Vector3 tip)) continue;
                bool hot = sizing ? sizeHandle == k : hoverHandle == k;
                Color axis = k == 2 ? Handles.yAxisColor : Handles.xAxisColor;
                Handles.color = hot ? Handles.selectedColor : axis;
                Handles.DrawAAPolyLine(hot ? 4f : 3f, root, tip);
                float size = HandleUtility.GetHandleSize(tip) * (hot ? 0.09f : 0.07f);
                Handles.CubeHandleCap(0, tip, Quaternion.LookRotation(tip - root), size, EventType.Repaint);
            }
            Handles.zTest = oldZTest;
            if (!sizing && hoverHandle >= 0) EditorGUIUtility.AddCursorRect(new Rect(mouse.x - 12f, mouse.y - 12f, 24f, 24f), MouseCursor.ScaleArrow);
        }

        private void DrawSizeLabel(Vector2 mouse)
        {
            if (Pattern == null || sizePiece < 0 || sizePiece >= Pattern.pieces.Count) return;
            bool changed = sizeTo != sizeFrom;
            string text = Pattern.pieces[sizePiece].name + ": " + sizeTo.x.ToString("0.0") + " cm wide, " + sizeTo.y.ToString("0.0") + " cm long";
            string hint = sizeAxis == SizeAxis.Across ? "\nDrag out to widen, in to narrow" : sizeAxis == SizeAxis.Down ? "\nDrag down to lengthen, up to shorten" : "\nDrag across it to widen, down it to lengthen";
            text += changed ? "\n(was " + sizeFrom.x.ToString("0.0") + " x " + sizeFrom.y.ToString("0.0") + " cm" + (sizePreviewing ? "; release to fit it" + (Fit.Sewn ? ")" : ", laid out again)") : ")") : hint;
            bool wider = !Mathf.Approximately(sizeTo.x, sizeFrom.x), longer = !Mathf.Approximately(sizeTo.y, sizeFrom.y);
            if (wider && sizeWidthBy != null) text += "\n" + sizeWidthBy;
            if (longer && sizeLengthBy != null) text += "\n" + sizeLengthBy;
            var content = new GUIContent(text);
            GUIStyle style = EditorStyles.helpBox;
            Vector2 size = style.CalcSize(content);
            Handles.BeginGUI();
            GUI.Label(new Rect(mouse.x + 18f, mouse.y + 8f, size.x + 4f, size.y + 2f), content, style);
            Handles.EndGUI();
        }

        /// <summary>
        /// How far the cloth moves (m) for one centimetre across (x) and up (y) its piece as drawn, on a triangle: its
        /// corners' positions and their places as drawn. False for a triangle with no area as drawn.
        /// </summary>
        public static bool ClothAxes(Vector3 a, Vector3 b, Vector3 c, Vector2 da, Vector2 db, Vector2 dc, out Vector3 x, out Vector3 y)
        {
            Vector2 e1 = db - da, e2 = dc - da;
            Vector3 f1 = b - a, f2 = c - a;
            float det = e1.x * e2.y - e2.x * e1.y;
            x = y = Vector3.zero;
            if (Mathf.Abs(det) < 1e-9f) return false;
            x = (f1 * e2.y - f2 * e1.y) / det;
            y = (f2 * e1.x - f1 * e2.x) / det;
            return true;
        }

        /// <summary>
        /// The width and length (cm, as cut) a Size drag gives. The pointer's move is read along the piece's own directions
        /// on screen (<paramref name="across"/> and <paramref name="along"/>: screen points for one centimetre as drawn across
        /// it and up it, at the point pressed). Across, away from the middle on the side pressed (<paramref name="side"/>: +1
        /// its right as drawn, -1 its left), widens it, both sides moving by the drag; down it, towards the hem, lengthens it,
        /// the top staying where it is laid. <paramref name="axis"/> Free changes only the direction moved further; a handle's
        /// Across or Down only that one. With <paramref name="keepProportions"/> (Shift) both change by that one's factor. A
        /// piece seen edge on reads right as wider and down as longer, at <see cref="SizeFallbackPointsPerCm"/>. Rounded to
        /// <see cref="SizeStepCm"/> (to 1% keeping proportions).
        /// </summary>
        public static Vector2 SizeAfterDrag(Vector2 from, Vector2 move, Vector2 across, Vector2 along, float side, PieceScale scale, bool keepProportions, SizeAxis axis = SizeAxis.Free)
        {
            if (from.x <= 0f || from.y <= 0f) return from;
            float a, b, pointsAcross, pointsAlong;
            float det = across.x * along.y - along.x * across.y;
            if (across.sqrMagnitude > 1e-6f && along.sqrMagnitude > 1e-6f && Mathf.Abs(det) > 0.05f * across.magnitude * along.magnitude)
            {
                a = (move.x * along.y - along.x * move.y) / det;
                b = (across.x * move.y - move.x * across.y) / det;
                pointsAcross = Mathf.Abs(a) * across.magnitude;
                pointsAlong = Mathf.Abs(b) * along.magnitude;
            }
            else
            {
                a = move.x / SizeFallbackPointsPerCm * side;
                b = -move.y / SizeFallbackPointsPerCm;
                pointsAcross = Mathf.Abs(move.x);
                pointsAlong = Mathf.Abs(move.y);
            }
            float width = from.x + 2f * a * side * scale.sx;
            float length = from.y - b * scale.sy;
            bool acrossWins = axis == SizeAxis.Across || (axis == SizeAxis.Free && pointsAcross >= pointsAlong);
            if (keepProportions)
            {
                float k = acrossWins ? width / from.x : length / from.y;
                k = Mathf.Clamp(Mathf.Round(k * 100f) / 100f, 1f / PieceSizing.MaxFactor, PieceSizing.MaxFactor);
                k = Mathf.Max(k, PieceSizing.MinCm / Mathf.Min(from.x, from.y));
                return from * k;
            }
            return acrossWins ? new Vector2(Snap(width, from.x), from.y) : new Vector2(from.x, Snap(length, from.y));
        }

        /// <summary>A dragged width or length: unchanged within half a step, else rounded to the step, within the size limits.</summary>
        private static float Snap(float value, float from)
        {
            if (Mathf.Abs(value - from) < SizeStepCm * 0.5f) return from;
            float snapped = Mathf.Round(value / SizeStepCm) * SizeStepCm;
            return Mathf.Clamp(snapped, Mathf.Max(PieceSizing.MinCm, from / PieceSizing.MaxFactor), from * PieceSizing.MaxFactor);
        }
    }
}
