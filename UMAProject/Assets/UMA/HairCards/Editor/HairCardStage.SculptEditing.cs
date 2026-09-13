using System;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairCardStage
    {
        // Deliberately transient: opening a groom/domain reload always starts in final preview.
        [NonSerialized] private string editingGroupId, editingLayerId;
        [NonSerialized] private string brushEvaluationEditLayerId;
        [NonSerialized] private int brushEvaluationRevision = -1;
        [NonSerialized] private bool sculptPreviewPending;

        internal bool IsLayerEditing => editingGroupId == ActiveGroup?.Id &&
            !string.IsNullOrEmpty(editingLayerId) && editingLayerId == ActiveLayer?.Id;

        internal bool CanEditLayer => ActiveGroup is { enabled: true, visible: true, locked: false } &&
            ActiveLayer is { visible: true, locked: false, opacity: > 0.0001f };

        internal string SculptEditingStatus => sceneTool == HairSceneTool.Erase && CanEraseGuides
            ? "Erase Guides — deletes whole authored guides across all sculpt layers. Frozen guides are protected. Undo restores the stroke."
            : IsLayerEditing
            ? HasDownstreamSculptOperations
                ? $"Editing {ActiveLayer.name} — its modifiers and later operations are temporarily bypassed."
                : $"Editing {ActiveLayer.name} — final sculpt edit point. Earlier modifiers remain applied."
            : "Final preview — use Edit This Layer for upstream changes, or add a finishing layer to sculpt this result.";

        internal bool BeginLayerEditing()
        {
            if (!CanEditLayer) return false;
            EndGravitySimulation(); ReleaseSceneInputCapture(true);
            editingGroupId = ActiveGroup.Id; editingLayerId = ActiveLayer.Id;
            activeModifierId = null; soloLayerId = null;
            activeNodeKey = HairGroomNodes.Key(HairGroomNodeKind.Layer, activeLayerId);
            sceneTool = IsGroomTool(sceneTool) ? sceneTool : IsGroomTool(lastGroomTool) ? lastGroomTool : HairSceneTool.Comb;
            workflowStep = HairWorkflowStep.Groom;
            RememberToolForStep(workflowStep, sceneTool);
            CaptureUnityToolState();
            InvalidateSculptEditingPreview();
            actionStatus = SculptEditingStatus;
            return true;
        }

        internal void ReturnToFinalPreview()
        {
            EndGravitySimulation(); ReleaseSceneInputCapture(true);
            editingGroupId = editingLayerId = null;
            soloLayerId = null;
            sceneTool = HairSceneTool.Select;
            InvalidateSculptEditingPreview();
            actionStatus = "Final preview restored. Select an edit point before sculpting.";
        }

        private void ClearLayerEditing()
        {
            if (editingLayerId == null) return;
            editingGroupId = editingLayerId = null;
            InvalidateSculptEditingPreview();
        }

        private void InvalidateSculptEditingPreview()
        {
            // Never let a hit-test or handle use curves from the previous edit point while
            // the (more expensive) card rebuild is queued. No asset or enabled flag changes.
            brushEvaluationEditLayerId = null;
            sculptPreviewPending = true;
            curveBrushEntries.Clear(); displayGuideCurves.Clear(); displayGuidePolylines.Clear();
            if (hairRenderer != null) hairRenderer.enabled = false;
            cardHighlightLines = Array.Empty<Vector3>();
            if (groom != null) RebuildInteractiveGuidePreview(EditorApplication.timeSinceStartup);
            QueuePreviewChange(HairPreviewChange.Evaluation, true);
            RepaintAll();
        }

        internal bool HasDownstreamSculptOperations
        {
            get
            {
                HairGroup group = ActiveGroup; HairSculptLayer selected = ActiveLayer;
                if (group == null || selected == null) return true;
                bool after = false;
                for (int section = 0; section < 2; section++)
                {
                    if (section == 1 && after && (group.modifiers.Exists(m => m != null && m.enabled && m.weight > 0f) ||
                        group.constraints.Exists(c => c != null && c.enabled))) return true;
                    foreach (HairSculptLayer layer in group.sculptLayers)
                    {
                        if (layer == null || layer.afterGroupOperations != (section == 1)) continue;
                        if (layer == selected) after = true;
                        if (!after || !layer.visible || layer.opacity <= 0f) continue;
                        if (layer != selected || layer.modifiers.Exists(m => m != null && m.enabled && m.weight > 0f)) return true;
                    }
                }
                return false;
            }
        }

        private bool EnsureSculptEditContext()
        {
            if (!CanEditLayer || ActiveModifier != null)
            { actionStatus = "Select an active, unlocked sculpt layer in Hair Nodes before sculpting."; return false; }
            if (!IsLayerEditing)
            {
                if (HasDownstreamSculptOperations || sceneTool == HairSceneTool.Select)
                { actionStatus = SculptEditingStatus; return false; }
                // A terminal, unmodified pass has exactly the same geometry as the final
                // preview, so entering its edit point does not cause an unexpected jump.
                BeginLayerEditing();
            }
            if (brushEvaluationEditLayerId != editingLayerId ||
                (!strokeActive && brushEvaluationRevision != EditorUtility.GetDirtyCount(groom)))
                RebuildInteractiveGuidePreview(EditorApplication.timeSinceStartup);
            return IsLayerEditing && brushEvaluationEditLayerId == editingLayerId;
        }

        internal bool IsNodeTemporarilyBypassed(HairGroomNode node)
        {
            if (!IsLayerEditing || node?.Group != ActiveGroup) return false;
            if (node.Kind == HairGroomNodeKind.Constraints || node.Kind == HairGroomNodeKind.Constraint ||
                node.Kind == HairGroomNodeKind.LegacyModifiers) return !ActiveLayer.afterGroupOperations;
            if (node.Layer == null) return false;
            if (node.Layer == ActiveLayer) return node.Modifier != null;
            if (node.Layer.afterGroupOperations != ActiveLayer.afterGroupOperations) return node.Layer.afterGroupOperations;
            return ActiveGroup.sculptLayers.IndexOf(node.Layer) > ActiveGroup.sculptLayers.IndexOf(ActiveLayer);
        }

        private void DrawSculptEditingBanner(float width)
        {
            if (workflowStep != HairWorkflowStep.Groom) return;
            bool narrow = width < 420f;
            GUILayout.BeginArea(new Rect(12f, 66f, width, narrow ? 104f : 72f), GUIContent.none, EditorStyles.helpBox);
            GUILayout.Label(SculptEditingStatus, EditorStyles.wordWrappedMiniLabel);
            if (!narrow) GUILayout.BeginHorizontal();
            if (IsLayerEditing)
            {
                if (GUILayout.Button("Return to Final Preview")) ReturnToFinalPreview();
            }
            else
            {
                using (new EditorGUI.DisabledScope(!CanEditLayer))
                    if (GUILayout.Button("Edit This Layer")) BeginLayerEditing();
                using (new EditorGUI.DisabledScope(ActiveGroup == null || ActiveGroup.locked || !ActiveGroup.enabled || !ActiveGroup.visible))
                    if (GUILayout.Button("Add Finishing Sculpt Layer")) HairGroomNodeWindow.AddFinishingSculptLayer(this, ActiveGroup);
            }
            if (!narrow) GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static int BrushPointCount(CurveBrushEntry entry, HairSceneTool tool) =>
            tool == HairSceneTool.Freeze ? entry.Guide.points.Count : entry.SourcePoints.Length;

        private static void CopyEditablePoints(CurveBrushEntry entry)
        {
            EnsurePointBuffers(entry, entry.SourcePoints.Length);
            Array.Copy(entry.SourcePoints, entry.OriginalPoints, entry.SourcePoints.Length);
            Array.Copy(entry.SourcePoints, entry.TargetPoints, entry.SourcePoints.Length);
        }
    }
}
