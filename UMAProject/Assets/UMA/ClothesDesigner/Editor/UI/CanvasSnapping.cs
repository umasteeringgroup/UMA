using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>Which way a move on the Pattern Canvas may go.</summary>
    public enum CanvasMoveAxis
    {
        Free = 0,
        /// <summary>Across only: the move's up-down part is dropped.</summary>
        Horizontal = 1,
        /// <summary>Up and down only.</summary>
        Vertical = 2
    }

    /// <summary>A guide the canvas draws while a move is snapped: a moving point lined up with a target (cm).</summary>
    public struct CanvasSnapGuide
    {
        /// <summary>True: lined up across (same x, a vertical guide); false: lined up in height (same y, a horizontal guide).</summary>
        public bool vertical;
        public Vector2 moving, target;
    }

    /// <summary>
    /// The Pattern Canvas's aids for moving things, in centimetres as drawn: keeping a move to one axis, and snapping it so a
    /// moving point lines up with a point nearby, across and in height separately, as drawing programs do.
    /// </summary>
    public static class CanvasSnapping
    {
        /// <summary>How close, on screen, a moving point must come to a target's line to snap to it (points).</summary>
        public const float SnapPixels = 8f;

        /// <summary>The axis a move keeps to: the one chosen, or, with <paramref name="dominant"/> (Ctrl held), the one it moved further along.</summary>
        public static CanvasMoveAxis AxisFor(Vector2 move, CanvasMoveAxis chosen, bool dominant)
        {
            if (chosen != CanvasMoveAxis.Free || !dominant) return chosen;
            return Mathf.Abs(move.x) >= Mathf.Abs(move.y) ? CanvasMoveAxis.Horizontal : CanvasMoveAxis.Vertical;
        }

        /// <summary>A move kept to an axis.</summary>
        public static Vector2 Constrain(Vector2 move, CanvasMoveAxis axis) =>
            axis == CanvasMoveAxis.Horizontal ? new Vector2(move.x, 0f) : axis == CanvasMoveAxis.Vertical ? new Vector2(0f, move.y) : move;

        /// <summary>
        /// A move snapped so one of the <paramref name="moving"/> points (where they started) lines up with one of the
        /// <paramref name="targets"/>: across (same x) when within <paramref name="reach"/>, and in height (same y) likewise,
        /// the nearest on each. An axis the move is kept off (<paramref name="axis"/>) is not snapped. The alignments made
        /// are added to <paramref name="guides"/>.
        /// </summary>
        public static Vector2 Snap(Vector2 move, IReadOnlyList<Vector2> moving, IReadOnlyList<Vector2> targets, float reach, CanvasMoveAxis axis, List<CanvasSnapGuide> guides)
        {
            guides?.Clear();
            if (moving == null || targets == null || moving.Count == 0 || targets.Count == 0 || reach <= 0f) return move;
            bool snapX = axis != CanvasMoveAxis.Vertical, snapY = axis != CanvasMoveAxis.Horizontal;
            float bestX = reach, bestY = reach, dx = 0f, dy = 0f;
            int movingX = -1, targetX = -1, movingY = -1, targetY = -1;
            for (int m = 0; m < moving.Count; m++)
            {
                Vector2 at = moving[m] + move;
                for (int t = 0; t < targets.Count; t++)
                {
                    Vector2 target = targets[t];
                    if (snapX)
                    {
                        float d = target.x - at.x;
                        if (Mathf.Abs(d) < bestX) { bestX = Mathf.Abs(d); dx = d; movingX = m; targetX = t; }
                    }
                    if (snapY)
                    {
                        float d = target.y - at.y;
                        if (Mathf.Abs(d) < bestY) { bestY = Mathf.Abs(d); dy = d; movingY = m; targetY = t; }
                    }
                }
            }
            Vector2 snapped = move + new Vector2(movingX >= 0 ? dx : 0f, movingY >= 0 ? dy : 0f);
            if (guides != null)
            {
                if (movingX >= 0) guides.Add(new CanvasSnapGuide { vertical = true, moving = moving[movingX] + snapped, target = targets[targetX] });
                if (movingY >= 0) guides.Add(new CanvasSnapGuide { vertical = false, moving = moving[movingY] + snapped, target = targets[targetY] });
            }
            return snapped;
        }
    }
}
