using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairCardStage
    {
        private readonly HashSet<string> eraseGuideIds = new HashSet<string>(StringComparer.Ordinal);
        private int erasedGuidesInStroke;

        // Deletion has no deformation writeback: the displayed final result is a safe hit target,
        // even when downstream modifiers make that result unsafe for an upstream sculpt stroke.
        internal bool CanEraseGuides => CanEditLayer && ActiveModifier == null &&
            activeNodeKey == HairGroomNodes.Key(HairGroomNodeKind.Layer, activeLayerId);

        internal Vector3 EraseFallbackCenter(Ray ray, Vector3 worldNormal, Vector3 pivot)
        {
            // A stroke may begin in empty space and sweep onto hair. Anchor its camera-facing
            // plane to the scalp if available, otherwise to the current Scene view focus depth.
            if (TryRaycastSourceSurface(ray, out HairSurfaceHit hit)) return hit.WorldPoint;
            return new Plane(worldNormal, pivot).Raycast(ray, out float enter) ? ray.GetPoint(enter) : pivot;
        }

        private bool EraseBrushTouches(CurveBrushEntry entry, Vector3 start, Vector3 end, Vector3 normal)
        {
            HairGuide guide = entry.Guide;
            if (guide == null || !guide.enabled || !GuideInEditScope(guide) || guide.points == null) return false;
            // Whole-guide deletion cannot honour a partially frozen point by deleting only a fraction.
            // Check authored points as well as the evaluated lattice (resampling may miss a frozen point).
            foreach (HairGuidePoint point in guide.points)
                if (point != null && point.freeze > 0f) return false;
            foreach (HairGuidePoint point in entry.Controls)
                if (point != null && point.freeze > 0f) return false;
            float radius = brushRadius * entry.PoseRadiusScale;
            if (!BrushBoundsOverlap(entry.PosedBounds, (start + end) * 0.5f,
                radius + Vector3.Distance(start, end) * 0.5f, normal)) return false;
            for (int i = 1; i < entry.PosedPoints.Length; i++)
            {
                Vector3 point = HairEraseBrushUtility.ClosestGuidePoint(start, end,
                    entry.PosedPoints[i - 1], entry.PosedPoints[i], normal, affectThroughDepth, out float square);
                if (square <= radius * radius &&
                    (brushScope != HairBrushScope.VisibleHair || IsPosedPointVisible(point))) return true;
            }
            return false;
        }

        private void EraseGuidesAt(Vector3 start, Vector3 end, Vector3 normal)
        {
            if (!CanEraseGuides || !float.IsFinite(start.sqrMagnitude) ||
                !float.IsFinite(end.sqrMagnitude) || !float.IsFinite(normal.sqrMagnitude)) return;
            eraseGuideIds.Clear();
            foreach (CurveBrushEntry entry in curveBrushEntries)
                if (EraseBrushTouches(entry, start, end, normal)) eraseGuideIds.Add(entry.Guide.Id);
            if (eraseGuideIds.Count == 0) return;
            BeginStroke("Erase Hair Guides");
            // Record the groom once at stroke start, not once per guide or mouse event.
            erasedGuidesInStroke += HairGroomCommands.RemoveGuidesInRecordedOperation(ActiveGroup, eraseGuideIds);
            foreach (string id in eraseGuideIds)
            {
                selectedGuideSet.Remove(id); brushHighlights.Remove(id);
                displayGuideCurves.Remove(id); displayGuidePolylines.Remove(id);
                retainedBrushEntries.Remove(id); activeBrushBufferIds.Remove(id);
                strokeLayerDeltas.Remove(id);
            }
            selectedGuideIds.RemoveAll(id => eraseGuideIds.Contains(id));
            curveBrushEntries.RemoveAll(entry => eraseGuideIds.Contains(entry.Guide.Id));
            if (activeGuideId != null && eraseGuideIds.Contains(activeGuideId))
            { activeGuideId = null; activeGuidePoint = -1; }
            // Do not show old children/cards between release and the deferred rebuild.
            generationPreview = null;
            sculptPreviewPending = true;
            cardHighlightLines = Array.Empty<Vector3>();
            EditorUtility.SetDirty(groom);
            actionStatus = $"Erasing guides: {erasedGuidesInStroke:N0} removed. Undo restores the entire stroke.";
            RepaintAll();
        }
    }

    internal static class HairEraseBrushUtility
    {
        // Closest points between the swept brush centre segment and a guide segment. Testing the
        // sweep, rather than only event positions, prevents holes when the mouse moves quickly.
        internal static Vector3 ClosestGuidePoint(Vector3 brushStart, Vector3 brushEnd,
            Vector3 guideStart, Vector3 guideEnd, Vector3 normal, bool throughDepth, out float square)
        {
            Vector3 u = brushEnd - brushStart, v = guideEnd - guideStart, w = brushStart - guideStart;
            if (throughDepth && normal.sqrMagnitude > 1e-12f)
            {
                normal.Normalize();
                u = Vector3.ProjectOnPlane(u, normal);
                v = Vector3.ProjectOnPlane(v, normal);
                w = Vector3.ProjectOnPlane(w, normal);
            }
            float a = Vector3.Dot(u, u), b = Vector3.Dot(u, v), c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w), e = Vector3.Dot(v, w);
            float s, t;
            if (a <= 1e-12f) { s = 0f; t = c > 1e-12f ? Mathf.Clamp01(e / c) : 0f; }
            else if (c <= 1e-12f) { t = 0f; s = Mathf.Clamp01(-d / a); }
            else
            {
                float denominator = a * c - b * b;
                s = denominator > 1e-6f * a * c ? Mathf.Clamp01((b * e - c * d) / denominator) : 0f;
                t = (b * s + e) / c;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-d / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - d) / a); }
            }
            square = (w + u * s - v * t).sqrMagnitude;
            return Vector3.Lerp(guideStart, guideEnd, t);
        }
    }
}
