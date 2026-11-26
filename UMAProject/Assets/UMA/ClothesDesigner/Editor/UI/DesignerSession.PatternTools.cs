using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>The Dart, Pleat and Draw Piece tools: pattern work done on the character in the Scene view.</summary>
    public sealed partial class DesignerSession
    {
        /// <summary>What the last Dart, Pleat or Draw Piece action did, or why it did nothing.</summary>
        public string PatternToolMessage;
        /// <summary>The points of the outline being drawn on the body (renderer space).</summary>
        public readonly List<Vector3> BodyOutline = new List<Vector3>();

        private bool patternPressed;
        private int patternPiece = -1;
        private Vector2 patternStart, patternEnd;
        private Vector3 patternStartPoint, patternEndPoint;
        private bool patternMoved;
        private bool bodyHoverValid;
        private Vector3 bodyHover;

        /// <summary>
        /// The cloth under a ray: which piece, the point on it as drawn (cm) and in the scene. The point as drawn is
        /// interpolated from the hit triangle's corners, each of which knows its place on its piece.
        /// </summary>
        public bool PatternPointAt(Ray ray, out int piece, out Vector2 drawn, out Vector3 point) => PatternPointAt(ray, out piece, out drawn, out point, out _);

        /// <summary>As <see cref="PatternPointAt(Ray, out int, out Vector2, out Vector3)"/>, with the hit triangle (the index of its first corner in the preview's triangle list).</summary>
        public bool PatternPointAt(Ray ray, out int piece, out Vector2 drawn, out Vector3 point, out int triangle)
        {
            piece = -1; drawn = Vector2.zero; point = Vector3.zero; triangle = -1;
            if (!Fit.HasCloth || previewTriangles == null) return false;
            Vector3[] p = Fit.Positions;
            float best = float.MaxValue;
            for (int t = 0; t < previewTriangles.Length; t += 3)
            {
                int ia = previewTriangles[t], ib = previewTriangles[t + 1], ic = previewTriangles[t + 2];
                Vector3 a = p[ia], b = p[ib], c = p[ic];
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 h = Vector3.Cross(ray.direction, e2);
                float det = Vector3.Dot(e1, h);
                if (Mathf.Abs(det) < 1e-12f) continue;
                float inv = 1f / det;
                Vector3 s = ray.origin - a;
                float u = inv * Vector3.Dot(s, h);
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float w = inv * Vector3.Dot(ray.direction, q);
                if (w < 0f || u + w > 1f) continue;
                float dist = inv * Vector3.Dot(e2, q);
                if (dist <= 0f || dist >= best) continue;
                best = dist;
                triangle = t;
                point = ray.origin + ray.direction * dist;
                piece = Fit.Mesh.piece[ia];
                drawn = Fit.Mesh.drawn[ia] * (1f - u - w) + Fit.Mesh.drawn[ib] * u + Fit.Mesh.drawn[ic] * w;
            }
            return piece >= 0;
        }

        private void PatternToolSceneGUI(SceneView view, Event e, int id)
        {
            Color ink = Tool == ClothTool.Dart ? new Color(1f, 0.55f, 0.85f) : new Color(0.55f, 0.8f, 1f);
            if (e.type == EventType.Repaint)
            {
                Handles.color = ink;
                if (patternPressed)
                {
                    Handles.DotHandleCap(0, patternStartPoint, Quaternion.identity, HandleUtility.GetHandleSize(patternStartPoint) * 0.025f, EventType.Repaint);
                    if (patternMoved) Handles.DrawAAPolyLine(4f, patternStartPoint, patternEndPoint);
                }
                else if (hoverValid) Handles.DotHandleCap(0, hoverPoint, Quaternion.identity, HandleUtility.GetHandleSize(hoverPoint) * 0.02f, EventType.Repaint);
            }
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0) break;
                    if (!PatternPointAt(HandleUtility.GUIPointToWorldRay(e.mousePosition), out patternPiece, out patternStart, out patternStartPoint)) break;
                    patternPressed = true;
                    patternMoved = false;
                    patternEnd = patternStart;
                    patternEndPoint = patternStartPoint;
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    if (PatternPointAt(HandleUtility.GUIPointToWorldRay(e.mousePosition), out int piece, out Vector2 drawn, out Vector3 point) && piece == patternPiece)
                    {
                        patternEnd = drawn;
                        patternEndPoint = point;
                        patternMoved = Vector2.Distance(patternStart, patternEnd) >= 1f;
                    }
                    view.Repaint();
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    if (patternPressed) FinishPatternTool(e.shift);
                    patternPressed = false;
                    e.Use();
                    break;
                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape && patternPressed)
                    {
                        patternPressed = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// Adds the dart or pleat to the pattern, then builds the cloth again keeping the drape (carried over through
        /// the pattern) and simulates, so the new dart or pleat is sewn on the body.
        /// </summary>
        private void FinishPatternTool(bool shift)
        {
            if (Pattern == null) return;
            Undo.RecordObject(Pattern, Tool == ClothTool.Dart ? "Add Dart on the Cloth" : "Add Pleat on the Cloth");
            bool added;
            string message;
            if (Tool == ClothTool.Dart) added = PatternOnBody.AddDartAt(Pattern, patternPiece, patternStart, patternMoved ? patternEnd : patternStart, shift, out message) != null;
            else added = PatternOnBody.AddPleatAt(Pattern, patternPiece, patternStart, out message) != null;
            PatternToolMessage = message;
            if (added) ApplyPatternEdit();
            NotifyChanged();
        }

        /// <summary>Takes up a pattern edit made in the Scene view: mirrors follow, the cloth is built again keeping its drape, and it simulates.</summary>
        private void ApplyPatternEdit()
        {
            PatternEditing.UpdateLinkedMirrors(Pattern);
            EditorUtility.SetDirty(Pattern);
            bool sewn = Fit.HasCloth && Fit.Sewn;
            RebuildCloth(sewn);
            if (Fit.HasCloth && sewn) SetRunning(true);
        }

        private void DrawPieceSceneGUI(SceneView view, bool pointerOverPanel)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.Repaint)
            {
                Handles.color = new Color(1f, 0.85f, 0.25f);
                for (int i = 0; i < BodyOutline.Count; i++)
                {
                    Handles.DotHandleCap(0, BodyOutline[i], Quaternion.identity, HandleUtility.GetHandleSize(BodyOutline[i]) * (i == 0 ? 0.03f : 0.02f), EventType.Repaint);
                    if (i > 0) Handles.DrawAAPolyLine(3f, BodyOutline[i - 1], BodyOutline[i]);
                }
                if (bodyHoverValid && BodyOutline.Count > 0)
                {
                    Handles.color = new Color(1f, 0.85f, 0.25f, 0.5f);
                    Handles.DrawAAPolyLine(2f, BodyOutline[BodyOutline.Count - 1], bodyHover);
                    if (BodyOutline.Count >= 2) Handles.DrawAAPolyLine(1f, bodyHover, BodyOutline[0]);
                }
            }
            if (e.alt || pointerOverPanel) { bodyHoverValid = false; return; }
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseMove:
                    bodyHoverValid = RaycastBody(HandleUtility.GUIPointToWorldRay(e.mousePosition), out bodyHover);
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                    if (e.button != 0) break;
                    if (!RaycastBody(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector3 hit)) break;
                    // Clicking the first point again closes the outline.
                    if (BodyOutline.Count >= 3 && Vector2.Distance(HandleUtility.WorldToGUIPoint(BodyOutline[0]), e.mousePosition) < 12f) FinishBodyPiece();
                    else { BodyOutline.Add(hit); PatternToolMessage = null; }
                    view.Repaint();
                    NotifyChanged();
                    e.Use();
                    break;
                case EventType.KeyDown:
                    if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && BodyOutline.Count >= 3) { FinishBodyPiece(); e.Use(); }
                    else if (e.keyCode == KeyCode.Backspace && BodyOutline.Count > 0) { BodyOutline.RemoveAt(BodyOutline.Count - 1); NotifyChanged(); e.Use(); }
                    else if (e.keyCode == KeyCode.Escape && BodyOutline.Count > 0) { BodyOutline.Clear(); NotifyChanged(); e.Use(); }
                    view.Repaint();
                    break;
            }
        }

        /// <summary>Makes a torso piece from the outline drawn on the body and adds it to the pattern.</summary>
        public void FinishBodyPiece()
        {
            if (Pattern == null || Body == null) { PatternToolMessage = "Open a pattern first, with the body measured."; return; }
            PatternPiece piece = PatternOnBody.PieceFromBody(Pattern, BodyOutline, Body.Landmarks, FitData != null ? FitData.clearanceCm * 0.01f + 0.005f : 0.01f, out string message);
            PatternToolMessage = message;
            if (piece == null) return;
            Undo.RecordObject(Pattern, "Draw Piece on the Body");
            Pattern.pieces.Add(piece);
            BodyOutline.Clear();
            EditorUtility.SetDirty(Pattern);
            RebuildCloth(false);
            NotifyChanged();
        }
    }
}
