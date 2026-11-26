using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    /// <summary>
    /// A group's paths edited together (<see cref="TexturePaintLayer.editPathsTogether"/>, on by default). While the group,
    /// or a path layer in it, is selected, every visible path in the group is drawn, faintly, in the Scene view and the
    /// UV window, the one under the pointer brighter; a plain click on one selects its layer for editing, as clicking it
    /// in the Layers panel does, and the others stay drawn. The selected path's own points and handles take their clicks
    /// first. Paths laid on the model in 3D (not in UV) are drawn and picked in the Scene view only. The trims a garment
    /// brings in are put in such a group, "Trim".
    /// </summary>
    public sealed partial class TexturePaintStageWindow
    {
        /// <summary>How close the pointer must be to a path to pick it (GUI points).</summary>
        internal const float GroupPathPickPoints = 8f;
        private static readonly Color GroupPathColor = new Color(1f, 0.85f, 0.35f, 0.5f);
        private static readonly Color GroupPathHoverColor = new Color(1f, 0.95f, 0.55f, 1f);

        /// <summary>
        /// The group whose paths are edited together: the selected layer when it is such a group, or the group holding the
        /// selected layer. Null when there is none, or its Edit Paths Together is off.
        /// </summary>
        internal static TexturePaintLayer PathGroupFor(TextureSet set)
        {
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return null;
            TexturePaintLayer active = set.layers[set.activeLayerIndex];
            if (active == null) return null;
            TexturePaintLayer group = active.kind == TexturePaintLayerKind.Group ? active : FindGroupLayer(set, active.parentId);
            return group != null && group.editPathsTogether ? group : null;
        }

        private static TexturePaintLayer FindGroupLayer(TextureSet set, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (TexturePaintLayer layer in set.layers)
                if (layer != null && layer.kind == TexturePaintLayerKind.Group && layer.id == id) return layer;
            return null;
        }

        /// <summary>
        /// The layer indices of the shown paths in a group, at any depth: visible path layers of two points or more, the
        /// group and every group between them visible too.
        /// </summary>
        internal static List<int> PathLayersIn(TextureSet set, TexturePaintLayer group)
        {
            var found = new List<int>();
            if (set == null || group == null || !group.visible) return found;
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || !layer.IsSplineLayer || !layer.visible || layer.spline == null || layer.spline.PointCount < 2) continue;
                string parent = layer.parentId;
                for (int depth = 0; depth < 64 && !string.IsNullOrEmpty(parent); depth++)
                {
                    TexturePaintLayer holder = FindGroupLayer(set, parent);
                    if (holder == null) break;
                    if (holder == group) { found.Add(i); break; }
                    if (!holder.visible) break;
                    parent = holder.parentId;
                }
            }
            return found;
        }

        /// <summary>The paths of the selected layer's group other than the selected layer itself, or none.</summary>
        private static List<int> OtherGroupPaths(TextureSet set)
        {
            List<int> paths = PathLayersIn(set, PathGroupFor(set));
            paths.Remove(set.activeLayerIndex);
            return paths;
        }

        private TextureSet SelectedTextureSet() =>
            controller?.Textures != null && (uint)selectedSurface < (uint)controller.Textures.Sets.Count
                ? controller.Textures.Sets[selectedSurface] : null;

        /// <summary>Selects a path layer for editing, as clicking it in the Layers panel does, and opens the groups above it there.</summary>
        private void SelectGroupPath(TextureSet set, int layerIndex)
        {
            TexturePaintLayer layer = set.layers[layerIndex];
            for (TexturePaintLayer holder = FindGroupLayer(set, layer.parentId); holder != null; holder = FindGroupLayer(set, holder.parentId))
                SetGroupExpanded(holder, true);
            set.activeLayerIndex = layerIndex;
            SyncActiveLayerSelection(set);
            ShowWorkspaceStatus("Editing '" + layer.name + "'. Click another path of the group to edit that one.");
            RepaintAll();
        }

        // ---- Scene view ----------------------------------------------------------------------------

        private Vector3[] GroupScenePath(TextureSet set, TexturePaintLayer layer)
        {
            layer.spline.EnsureControlPoints();
            return GetSurfaceHuggingCurve(set.surface, layer.spline) ?? new Vector3[0];
        }

        /// <summary>The group path under the pointer in the Scene view (its layer index), or -1.</summary>
        private int GroupScenePathAt(TextureSet set, List<int> paths)
        {
            int nearest = -1;
            float best = GroupPathPickPoints;
            foreach (int i in paths)
            {
                Vector3[] curve = GroupScenePath(set, set.layers[i]);
                for (int k = 1; k < curve.Length; k++)
                {
                    if (!IsFiniteScenePoint(curve[k - 1]) || !IsFiniteScenePoint(curve[k])) continue;
                    float d = HandleUtility.DistanceToLine(curve[k - 1], curve[k]);
                    if (d < best) { best = d; nearest = i; }
                }
            }
            return nearest;
        }

        /// <summary>Draws the group's other paths in the Scene view, the one under the pointer brighter.</summary>
        private void DrawGroupScenePaths(TextureSet set)
        {
            if (Event.current.type != EventType.Repaint) return;
            List<int> paths = OtherGroupPaths(set);
            if (paths.Count == 0) return;
            int hovered = GUIUtility.hotControl == 0 ? GroupScenePathAt(set, paths) : -1;
            Color previous = Handles.color;
            foreach (int i in paths)
            {
                Vector3[] curve = GroupScenePath(set, set.layers[i]);
                Handles.color = i == hovered ? GroupPathHoverColor : GroupPathColor;
                for (int k = 1; k < curve.Length; k++)
                    if (IsFiniteScenePoint(curve[k - 1]) && IsFiniteScenePoint(curve[k]))
                        Handles.DrawLine(curve[k - 1], curve[k], i == hovered ? 3f : 2f);
            }
            Handles.color = previous;
        }

        /// <summary>A plain click on one of the group's other paths in the Scene view selects it. True when it did.</summary>
        private bool TryPickGroupScenePath(Event current)
        {
            if (current.type != EventType.MouseDown || current.button != 0 || current.alt || current.shift || current.control || current.command) return false;
            if (GUIUtility.hotControl != 0) return false;
            TextureSet set = SelectedTextureSet();
            List<int> paths = OtherGroupPaths(set);
            if (paths.Count == 0) return false;
            int picked = GroupScenePathAt(set, paths);
            if (picked < 0) return false;
            SelectGroupPath(set, picked);
            current.Use();
            return true;
        }

        // ---- UV window --------------------------------------------------------------------------------

        /// <summary>A UV path's curve on the UV canvas (texture rect, v up); empty for a path laid on the model in 3D.</summary>
        internal static List<Vector2> UVPathCanvasPoints(TexturePaintSpline path, Rect textureRect)
        {
            var points = new List<Vector2>();
            if (path == null || path.worldSpace || path.PointCount < 2) return points;
            path.EnsureControlPoints();
            for (int segment = 0; segment < path.SegmentCount; segment++)
            {
                int next = (segment + 1) % path.PointCount;
                for (int step = 0; step <= 24; step++)
                {
                    if (segment > 0 && step == 0) continue;
                    path.EvaluateSegment(segment, next, step / 24f, out _, out Vector2 uv);
                    points.Add(new Vector2(textureRect.x + uv.x * textureRect.width, textureRect.y + (1f - uv.y) * textureRect.height));
                }
            }
            return points;
        }

        /// <summary>Which of some curves (by key) passes within <paramref name="reach"/> of a point, nearest first; -1 when none.</summary>
        internal static int NearestCurve(IReadOnlyList<(int key, List<Vector2> points)> curves, Vector2 at, float reach)
        {
            int nearest = -1;
            float best = reach;
            foreach ((int key, List<Vector2> points) in curves)
            {
                for (int k = 1; k < points.Count; k++)
                {
                    Vector2 a = points[k - 1], ab = points[k] - a;
                    float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(at - a, ab) / ab.sqrMagnitude) : 0f;
                    float d = Vector2.Distance(at, a + ab * t);
                    if (d < best) { best = d; nearest = key; }
                }
            }
            return nearest;
        }

        private static List<(int key, List<Vector2> points)> GroupUVPaths(TextureSet set, Rect textureRect)
        {
            var curves = new List<(int, List<Vector2>)>();
            foreach (int i in OtherGroupPaths(set))
            {
                List<Vector2> points = UVPathCanvasPoints(set.layers[i].spline, textureRect);
                if (points.Count > 1) curves.Add((i, points));
            }
            return curves;
        }

        /// <summary>Draws the group's other UV paths in the UV window, the one under the pointer brighter.</summary>
        private void DrawUVGroupPaths(TextureSet set, Rect textureRect)
        {
            if (set == null || Event.current.type != EventType.Repaint) return;
            List<(int key, List<Vector2> points)> curves = GroupUVPaths(set, textureRect);
            if (curves.Count == 0) return;
            int hovered = NearestCurve(curves, Event.current.mousePosition, GroupPathPickPoints);
            Color previous = Handles.color;
            foreach ((int key, List<Vector2> points) in curves)
            {
                var line = new Vector3[points.Count];
                for (int k = 0; k < line.Length; k++) line[k] = points[k];
                Handles.color = key == hovered ? GroupPathHoverColor : GroupPathColor;
                Handles.DrawAAPolyLine(key == hovered ? 3f : 2f, line);
            }
            Handles.color = previous;
        }

        /// <summary>
        /// A plain click on one of the group's other paths in the UV window selects it, unless it is on a point or handle
        /// of the selected path (<paramref name="activePath"/>). True when it did.
        /// </summary>
        private bool TryPickGroupUVPath(TextureSet set, Rect textureRect, Event current, TexturePaintSpline activePath)
        {
            if (current.type != EventType.MouseDown || current.button != 0 || current.alt || current.shift || current.control || current.command) return false;
            if (activePath != null)
            {
                if (FindSplinePointAt(textureRect, current.mousePosition) >= 0) return false;
                if (PathEditModeAllowsAdjust(pathEditMode) &&
                    FindSelectedUVSplineHandleAt(activePath, textureRect, current.mousePosition) != UVSplineHandleKind.None) return false;
            }
            int picked = NearestCurve(GroupUVPaths(set, textureRect), current.mousePosition, GroupPathPickPoints);
            if (picked < 0) return false;
            SelectGroupPath(set, picked);
            current.Use();
            return true;
        }

        // ---- The group's properties -----------------------------------------------------------------

        private void DrawGroupPathOptions(TextureSet set, TexturePaintLayer group)
        {
            bool together = EditorGUILayout.ToggleLeft(new GUIContent("Edit Paths Together",
                "While this group or a path in it is selected, show all the group's paths in the Scene view and the UV window; click one to edit it. Paths can still be selected one by one in the Layers panel."),
                group.editPathsTogether);
            if (together != group.editPathsTogether) SetEditPathsTogether(group, together);
            if (!group.editPathsTogether) return;
            int paths = PathLayersIn(set, group).Count;
            if (paths > 0)
                EditorGUILayout.HelpBox(paths + (paths == 1 ? " path" : " paths") + " in this group: click one in the Scene view or the UV window to edit it.", MessageType.None);
        }

        private void SetEditPathsTogether(TexturePaintLayer group, bool on)
        {
            bool before = group.editPathsTogether;
            group.editPathsTogether = on;
            PushLightweightCommand("Edit Paths Together",
                () => { group.editPathsTogether = before; RepaintAll(); },
                () => { group.editPathsTogether = on; RepaintAll(); });
            MarkDocumentDirty();
            RepaintAll();
        }
    }
}
