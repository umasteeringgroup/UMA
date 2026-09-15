using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairCardStage
    {
        [SerializeField] private string selectedFlowSplineId;
        [SerializeField] private int selectedFlowPoint = -1;
        private readonly List<HairSurfaceAnchor> flowDraft = new List<HairSurfaceAnchor>();
        private string flowGestureModifierId, flowGestureSplineId;
        private bool flowLeftSurface;
        private Vector2 flowPreviousMouse;
        private Vector2 flowListScroll;
        private readonly Vector3[] flowLine = new Vector3[2];

        internal HairFlowSpline SelectedFlowSpline => ActiveModifier?.type == HairModifierType.SplineFlow
            ? ActiveModifier.flowSplines.Find(item => item != null && item.Id == selectedFlowSplineId) : null;
        private bool CanEditFlow => ActiveModifier?.type == HairModifierType.SplineFlow &&
            ActiveGroup != null && !ActiveGroup.locked && ActiveGroup.enabled && ActiveGroup.visible &&
            (SelectedNode?.Population != null || (ActiveLayer != null && !ActiveLayer.locked));

        internal void SelectFlowSpline(string id)
        {
            if (flowGestureModifierId != null) ReleaseSceneInputCapture(true);
            selectedFlowSplineId = id; selectedFlowPoint = -1; RepaintAll();
        }

        internal void SetFlowTool(bool draw)
        {
            if (!CanEditFlow) return;
            if (SceneTool == (draw ? HairSceneTool.DrawFlow : HairSceneTool.EditFlow)) return;
            SceneTool = draw ? HairSceneTool.DrawFlow : HairSceneTool.EditFlow;
            RepaintAll();
        }

        private void CancelFlowGesture()
        {
            flowDraft.Clear(); flowGestureModifierId = flowGestureSplineId = null;
            flowLeftSurface = false;
        }

        internal void ChangeFlowSpline(string operation)
        {
            HairFlowSpline spline = SelectedFlowSpline;
            if (!CanEditFlow || spline == null) return;
            ReleaseSceneInputCapture(true);
            HairFlowSpline mirror = null;
            if (operation == "Mirror" && !TryMirrorFlowSpline(spline, out mirror))
            { actionStatus = "Mirror could not attach every point to a visible matching surface. No copy was added."; RepaintAll(); return; }
            if (operation == "Remove Point" && (selectedFlowPoint < 0 || selectedFlowPoint >= spline.points.Count || spline.points.Count <= 2)) return;
            Undo.RecordObject(groom, operation + " Hair Flow Spline");
            switch (operation)
            {
                case "Reverse": spline.points.Reverse(); selectedFlowPoint = -1; break;
                case "Duplicate":
                    HairFlowSpline copy = spline.Duplicate(); copy.name += " Copy";
                    ActiveModifier.flowSplines.Add(copy); selectedFlowSplineId = copy.Id; break;
                case "Mirror": ActiveModifier.flowSplines.Add(mirror); selectedFlowSplineId = mirror.Id; break;
                case "Remove": ActiveModifier.flowSplines.Remove(spline); selectedFlowSplineId = null; selectedFlowPoint = -1; break;
                case "Remove Point": spline.points.RemoveAt(selectedFlowPoint); selectedFlowPoint = -1; break;
            }
            HairGroomCommands.Commit(groom, HairPreviewChange.Evaluation);
            lastWorkspaceInteraction = EditorApplication.timeSinceStartup;
            RepaintAll();
        }

        private bool TryFlowAnchor(HairSurfaceHit hit, out HairSurfaceAnchor anchor)
        {
            anchor = default;
            if (!TryResolveTriangle(hit.TriangleIndex, out int submesh, out int triangle, out int a, out int b, out int c) ||
                (uint)a >= sourceVertices.Length || (uint)b >= sourceVertices.Length || (uint)c >= sourceVertices.Length) return false;
            Vector3 bary = HairMeshUtility.Barycentric(hit.SourcePoint, sourceVertices[a], sourceVertices[b], sourceVertices[c]);
            anchor = HairSurfaceAnchor.Create(groom.SourceMeshId, submesh, triangle, bary, 0f, hit.SourcePoint, hit.SourceNormal);
            return true;
        }

        // Mirror in source space, then attach the reflected path to real triangles. The visible
        // collider's flattened triangle IDs are NOT interchangeable with the full source IDs.
        internal bool TryMirrorFlowSpline(HairFlowSpline source, out HairFlowSpline mirror)
        {
            mirror = null;
            if (sourceSurfaceRaycaster == null || source?.points == null || source.points.Count < 2) return false;
            var copy = new HairFlowSpline { name = source.name + " Mirror", enabled = source.enabled };
            Vector3 planeNormal = groom.SymmetryPlaneNormal.normalized;
            float maxDistance = Mathf.Max(0.005f, Mathf.Min(0.05f, ActiveModifier?.flowRadius * 0.5f ?? 0.05f));
            bool distinct = false;
            foreach (HairSurfaceAnchor anchor in source.points)
            {
                Vector3 position = anchor.CachedLocalPosition;
                Vector3 reflected = position - 2f * Vector3.Dot(position - groom.SymmetryPlanePoint, planeNormal) * planeNormal;
                Vector3 reflectedNormal = Vector3.Reflect(anchor.CachedLocalNormal, planeNormal);
                if (!sourceSurfaceRaycaster.ClosestPoint(reflected, out HairMeshRaycastHit hit, maxDistance) ||
                    !sourceSurfaceRaycaster.TryGetTriangleVertices(hit.TriangleIndex, out int a, out int b, out int c) ||
                    !IsSourceVertexVisible(a) || !IsSourceVertexVisible(b) || !IsSourceVertexVisible(c)) return false;
                int submesh = 0, triangle = hit.TriangleIndex;
                while (submesh < groom.SourceMesh.subMeshCount && triangle >= (int)groom.SourceMesh.GetIndexCount(submesh) / 3)
                    triangle -= (int)groom.SourceMesh.GetIndexCount(submesh++) / 3;
                if (submesh >= groom.SourceMesh.subMeshCount) return false;
                Vector3 bary = HairMeshUtility.Barycentric(hit.Point, sourceVertices[a], sourceVertices[b], sourceVertices[c]);
                Vector3 normal = sourceNormals.Length == sourceVertices.Length
                    ? (sourceNormals[a] * bary.x + sourceNormals[b] * bary.y + sourceNormals[c] * bary.z).normalized : hit.Normal;
                if (Vector3.Dot(normal, reflectedNormal) <= 0f) return false;
                copy.points.Add(HairSurfaceAnchor.Create(groom.SourceMeshId, submesh, triangle, bary, 0f, hit.Point, normal));
                distinct |= (hit.Point - position).sqrMagnitude > 1e-10f;
            }
            if (!distinct) return false;
            copy.EnsureIntegrity(); mirror = copy; return true;
        }

        private Vector3 FlowWorldPoint(HairSurfaceAnchor anchor, bool lift = true)
        {
            Matrix4x4 pose = authoringPose != null && authoringPose.TryGetMatrix(anchor, out Matrix4x4 matrix)
                ? matrix : Matrix4x4.identity;
            Vector3 point = anchor.CachedLocalPosition + (lift ? anchor.CachedLocalNormal * 0.0008f : Vector3.zero);
            return (SourceToStageMatrix * pose).MultiplyPoint3x4(point);
        }

        private bool FlowPointVisible(HairSurfaceAnchor anchor, Vector2 mouse)
        {
            Vector3 point = FlowWorldPoint(anchor);
            Camera camera = SceneView.currentDrawingSceneView?.camera;
            if (camera != null && camera.WorldToViewportPoint(point).z <= 0f) return false;
            if (!depthTestGuides) return true;
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            return !TryRaycastSourceSurface(ray, out HairSurfaceHit hit) ||
                Vector3.Dot(point - ray.origin, ray.direction) <= Vector3.Dot(hit.WorldPoint - ray.origin, ray.direction) + 0.003f;
        }

        private int PickFlowPoint(HairFlowSpline spline, Vector2 mouse)
        {
            int best = -1; float distance = 12f * 12f;
            for (int i = 0; i < spline.points.Count; i++)
            {
                float candidate = (HandleUtility.WorldToGUIPoint(FlowWorldPoint(spline.points[i])) - mouse).sqrMagnitude;
                if (candidate < distance && FlowPointVisible(spline.points[i], mouse)) { distance = candidate; best = i; }
            }
            return best;
        }

        private int PickFlowSegment(HairFlowSpline spline, Vector2 mouse)
        {
            int best = -1; float distance = 14f * 14f;
            for (int i = 1; i < spline.points.Count; i++)
            {
                Vector2 a = HandleUtility.WorldToGUIPoint(FlowWorldPoint(spline.points[i - 1]));
                Vector2 b = HandleUtility.WorldToGUIPoint(FlowWorldPoint(spline.points[i]));
                Vector2 segment = b - a;
                float t = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(mouse - a, segment) / segment.sqrMagnitude) : 0f;
                float candidate = (mouse - Vector2.Lerp(a, b, t)).sqrMagnitude;
                if (candidate < distance && FlowPointVisible(spline.points[t < 0.5f ? i - 1 : i], mouse)) { distance = candidate; best = i; }
            }
            return best;
        }

        private void HandleFlowSplineEvent(SceneView view, Event current, Ray ray, int controlId)
        {
            if (!CanEditFlow)
            { actionStatus = "Select an unlocked Spline Flow modifier in the layer stack to edit its paths."; return; }
            if (current.type == EventType.KeyDown && !EditorGUIUtility.editingTextField &&
                (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace))
            { ChangeFlowSpline("Remove Point"); current.Use(); return; }
            if (current.type == EventType.MouseDown && CanCaptureSceneInput(controlId))
            {
                if (!TryRaycastSourceSurface(ray, out HairSurfaceHit hit) || !TryFlowAnchor(hit, out HairSurfaceAnchor anchor)) return;
                HairFlowSpline selected = SelectedFlowSpline;
                if (sceneTool == HairSceneTool.EditFlow)
                {
                    if (selected == null) { actionStatus = "Select a flow spline in the property box first."; return; }
                    int index = current.shift ? PickFlowSegment(selected, current.mousePosition) : PickFlowPoint(selected, current.mousePosition);
                    if (index < 0) return;
                    if (current.shift && selected.points.Count >= 256)
                    { actionStatus = "This path has 256 points. Remove a point before inserting another."; return; }
                    flowDraft.AddRange(selected.points); selectedFlowPoint = index;
                    if (current.shift) flowDraft.Insert(index, anchor);
                    flowGestureSplineId = selected.Id;
                }
                else { flowDraft.Add(anchor); flowGestureSplineId = null; selectedFlowPoint = -1; }
                flowGestureModifierId = ActiveModifier.Id; flowPreviousMouse = current.mousePosition;
                CaptureSceneInput(controlId); current.Use(); RepaintAll();
            }
            else if (current.type == EventType.MouseDrag && OwnsSceneInput(controlId) && flowGestureModifierId == ActiveModifier.Id)
            {
                if (sceneTool == HairSceneTool.DrawFlow) AppendFlowDrag(current.mousePosition, false);
                else if (TryRaycastSourceSurface(ray, out HairSurfaceHit hit) && TryFlowAnchor(hit, out HairSurfaceAnchor anchor))
                    flowDraft[selectedFlowPoint] = anchor;
                current.Use(); view.Repaint();
            }
            else if (current.type == EventType.MouseUp && OwnsSceneInput(controlId))
            {
                if (sceneTool == HairSceneTool.DrawFlow) AppendFlowDrag(current.mousePosition, true);
                CommitFlowGesture(); ReleaseSceneInputCapture(true); current.Use(); RepaintAll();
            }
        }

        private void AppendFlowDrag(Vector2 mouse, bool includeEnd)
        {
            if (flowLeftSurface || flowDraft.Count == 0) return;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(mouse, flowPreviousMouse) / 5f), 1, 64);
            for (int step = 1; step <= steps; step++)
            {
                Vector2 screen = Vector2.Lerp(flowPreviousMouse, mouse, step / (float)steps);
                if (!TryRaycastSourceSurface(HandleUtility.GUIPointToWorldRay(screen), out HairSurfaceHit hit) || !TryFlowAnchor(hit, out HairSurfaceAnchor anchor))
                { flowLeftSurface = true; actionStatus = "Path stopped at the surface edge. Release to keep it, or Esc to cancel."; break; }
                float distance = Vector3.Distance(anchor.CachedLocalPosition, flowDraft[flowDraft.Count - 1].CachedLocalPosition);
                if (distance < (includeEnd && step == steps ? 0.00001f : ActiveModifier.flowPointSpacing)) continue;
                if (flowDraft.Count >= 256)
                { actionStatus = "256-point path limit reached. Release to finish; increase Point spacing for longer paths."; break; }
                // Do not bridge discontinuous hit surfaces (e.g. ear to shoulder) on a quick drag.
                if (distance > Mathf.Max(ActiveModifier.flowPointSpacing * 8f, 0.04f))
                { flowLeftSurface = true; break; }
                flowDraft.Add(anchor);
            }
            flowPreviousMouse = mouse;
        }

        private void CommitFlowGesture()
        {
            if (!CanEditFlow || flowGestureModifierId != ActiveModifier.Id || flowDraft.Count < 2) return;
            HairFlowSpline spline = flowGestureSplineId == null ? null : SelectedFlowSpline;
            if (flowGestureSplineId != null && spline?.Id != flowGestureSplineId) return;
            Undo.RecordObject(groom, spline == null ? "Draw Hair Flow Spline" : "Edit Hair Flow Spline Point");
            bool created = spline == null;
            if (created)
            {
                spline = new HairFlowSpline { name = "Flow " + (ActiveModifier.flowSplines.Count + 1) };
                spline.EnsureIntegrity(); ActiveModifier.flowSplines.Add(spline);
            }
            spline.points.Clear(); spline.points.AddRange(flowDraft); selectedFlowSplineId = spline.Id;
            bool mirrored = false;
            if (created && ActiveModifier.flowMirrorDrawing && TryMirrorFlowSpline(spline, out HairFlowSpline mirror))
            { ActiveModifier.flowSplines.Add(mirror); mirrored = true; }
            actionStatus = created && ActiveModifier.flowMirrorDrawing && !mirrored
                ? "Flow saved. Mirror could not attach to a distinct, visible matching surface; no mirrored copy was added."
                : $"{spline.name}: {spline.points.Count} surface points. Flow runs from the green START to the orange END.";
            HairGroomCommands.Commit(groom, HairPreviewChange.Evaluation);
            lastWorkspaceInteraction = EditorApplication.timeSinceStartup;
        }

        private void DrawFlowSplineOverlays()
        {
            HairModifierSettings modifier = ActiveModifier;
            if (Event.current.type != EventType.Repaint || modifier?.type != HairModifierType.SplineFlow || ActiveGroup?.visible != true) return;
            bool editing = sceneTool == HairSceneTool.DrawFlow || sceneTool == HairSceneTool.EditFlow;
            if (!editing && (!showHelpers || !modifier.flowShowPaths)) return;
            CompareFunction previous = Handles.zTest;
            using (new Handles.DrawingScope(Matrix4x4.identity))
            {
                try
                {
                    Handles.zTest = depthTestGuides ? CompareFunction.LessEqual : CompareFunction.Always;
                    foreach (HairFlowSpline spline in modifier.flowSplines)
                    {
                        if (spline == null || (flowGestureSplineId == spline.Id && flowDraft.Count > 0)) continue;
                        DrawFlowPath(spline.points, spline.Id == selectedFlowSplineId, spline.enabled, spline.name);
                    }
                    if (flowGestureModifierId == modifier.Id) DrawFlowPath(flowDraft, true, true, "Drawing");
                }
                finally { Handles.zTest = previous; }
            }
        }

        private void DrawFlowPath(IReadOnlyList<HairSurfaceAnchor> points, bool selected, bool enabled, string label)
        {
            if (points.Count == 0) return;
            Color color = !enabled ? Color.gray : selected ? new Color(1f, 0.85f, 0.2f) : new Color(0.1f, 0.9f, 1f);
            Handles.color = color;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = FlowWorldPoint(points[i]);
                float size = HandleUtility.GetHandleSize(p) * 0.035f;
                if (i > 0)
                {
                    Vector3 previous = FlowWorldPoint(points[i - 1]);
                    flowLine[0] = previous; flowLine[1] = p;
                    Handles.color = color; Handles.DrawAAPolyLine(selected ? 4f : 2f, flowLine);
                    if (i == points.Count - 1 || i % Mathf.Max(1, points.Count / 5) == 0)
                    {
                        Vector3 direction = p - previous;
                        if (direction.sqrMagnitude > 1e-10f)
                            Handles.ConeHandleCap(0, Vector3.Lerp(previous, p, 0.5f), Quaternion.LookRotation(direction), size * 1.4f, EventType.Repaint);
                    }
                }
                if (i == 0 || i == points.Count - 1 || (selected && sceneTool == HairSceneTool.EditFlow))
                {
                    Handles.color = i == 0 ? Color.green : i == points.Count - 1 ? new Color(1f, 0.45f, 0.1f) :
                        i == selectedFlowPoint ? Color.white : color;
                    Handles.SphereHandleCap(0, p, Quaternion.identity, size * (i == selectedFlowPoint ? 1.4f : 1f), EventType.Repaint);
                }
                if (selected && (i == 0 || i == points.Count - 1) && FlowPointVisible(points[i], HandleUtility.WorldToGUIPoint(p)))
                    Handles.Label(p, i == 0 ? label + " START" : "END", EditorStyles.whiteMiniLabel);
            }
        }

        internal void DrawFlowSplineList(HairModifierSettings modifier)
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(flowListScroll,
                GUILayout.Height(Mathf.Clamp(modifier.flowSplines.Count * 24f + 6f, 32f, 150f))))
            {
                flowListScroll = scroll.scrollPosition;
                foreach (HairFlowSpline spline in modifier.flowSplines)
                {
                    if (spline == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool enabled = EditorGUILayout.Toggle(spline.enabled, GUILayout.Width(18f));
                        if (enabled != spline.enabled)
                        { Undo.RecordObject(groom, "Toggle Hair Flow Spline"); spline.enabled = enabled; HairGroomCommands.Commit(groom, HairPreviewChange.Evaluation); }
                        bool selected = spline.Id == selectedFlowSplineId;
                        if (GUILayout.Toggle(selected, new GUIContent(spline.name, "Select this path to edit its surface points"), "Button") && !selected)
                            SelectFlowSpline(spline.Id);
                        GUILayout.Label(spline.points.Count + " pts", EditorStyles.miniLabel, GUILayout.Width(46f));
                    }
                }
            }
        }
    }

    internal static class HairSplineFlowInspector
    {
        internal static void Draw(HairCardStage stage, HairModifierSettings modifier)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Surface Spline Flow", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float radius = EditorGUILayout.FloatField(new GUIContent("Influence radius", "Source-local units. Measured from each hair root to nearby paths; fades to zero at the boundary."), modifier.flowRadius);
            int neighbors = EditorGUILayout.IntSlider("Nearby splines", modifier.flowNeighbors, 1, 4);
            float direction = EditorGUILayout.Slider(new GUIContent("Follow direction", "0 changes only card facing. 1 steers hair along the paths while preserving roots and segment lengths."), modifier.flowDirection, 0f, 1f);
            float facing = EditorGUILayout.Slider(new GUIContent("Outward facing", "Aligns ribbon fronts to interpolated surface normals. Authored roll/Twist remains an offset."), modifier.flowFacing, 0f, 1f);
            float lift = EditorGUILayout.Slider("Lift from surface (°)", modifier.flowLift, 0f, 80f);
            float bank = EditorGUILayout.Slider("Facing bank (°)", modifier.flowBank, -180f, 180f);
            float spacing = EditorGUILayout.Slider(new GUIContent("Point spacing", "Distance between drawn surface points, in source units. Lower is more detailed; maximum 256 points per path."), modifier.flowPointSpacing, 0.001f, 0.05f);
            bool mirror = EditorGUILayout.Toggle(new GUIContent("Mirror new paths", "Creates an independent, surface-anchored copy across the groom symmetry plane on release. Both paths undo together."), modifier.flowMirrorDrawing);
            bool show = EditorGUILayout.Toggle("Show paths when not editing", modifier.flowShowPaths);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(stage.Groom, "Edit Hair Spline Flow");
                modifier.flowRadius = radius; modifier.flowNeighbors = neighbors;
                modifier.flowDirection = direction; modifier.flowFacing = facing; modifier.flowLift = lift; modifier.flowBank = bank;
                modifier.flowPointSpacing = spacing; modifier.flowMirrorDrawing = mirror; modifier.flowShowPaths = show;
                HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Evaluation);
            }
            EditorGUILayout.HelpBox("Draw from the part/root area toward the desired ends. Nearby paths blend directions, not positions: hair keeps its spacing and length. Green = START; orange = END. Increase Influence radius to reach more roots. Use Follow direction = 0 to orient existing cards without restyling them.", MessageType.Info);
            if (modifier.flowSplines.Count == 0) EditorGUILayout.LabelField("No paths yet — Draw New, then drag on the head in the Scene view.", EditorStyles.wordWrappedMiniLabel);
            stage.DrawFlowSplineList(modifier);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.DrawFlow, "Draw New", "Button")) stage.SetFlowTool(true);
                using (new EditorGUI.DisabledScope(stage.SelectedFlowSpline == null))
                    if (GUILayout.Toggle(stage.SceneTool == HairSceneTool.EditFlow, "Edit Points", "Button")) stage.SetFlowTool(false);
                if (GUILayout.Button("Done")) stage.SceneTool = HairSceneTool.Comb;
            }
            HairFlowSpline selected = stage.SelectedFlowSpline;
            using (new EditorGUI.DisabledScope(selected == null))
            {
                string name = EditorGUILayout.DelayedTextField("Selected path", selected?.name ?? "None");
                if (selected != null && name != selected.name)
                { Undo.RecordObject(stage.Groom, "Rename Hair Flow Spline"); selected.name = name; HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Display); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reverse")) stage.ChangeFlowSpline("Reverse");
                    if (GUILayout.Button("Duplicate")) stage.ChangeFlowSpline("Duplicate");
                    if (GUILayout.Button("Mirror")) stage.ChangeFlowSpline("Mirror");
                    if (GUILayout.Button("Remove")) stage.ChangeFlowSpline("Remove");
                }
                if (GUILayout.Button("Remove selected point (minimum 2)")) stage.ChangeFlowSpline("Remove Point");
            }
            EditorGUILayout.LabelField("Edit Points: drag handles on the surface; Shift-click near a path to insert; Delete removes a selected point. Release applies the preview as one Undo. Esc cancels the gesture. Mirrored paths are independent after creation. Paths use source space and follow the authoring pose, never the camera.", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
