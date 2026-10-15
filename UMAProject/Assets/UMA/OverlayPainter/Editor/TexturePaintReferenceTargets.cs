using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        [SerializeField] private List<string> visibleReferenceSlots = new List<string>();
        private readonly Dictionary<ReconstructedSurface, ReferenceDisplayMesh> referenceDisplayMeshes =
            new Dictionary<ReconstructedSurface, ReferenceDisplayMesh>();

        private sealed class ReferenceDisplayMesh
        {
            public string signature;
            public Mesh source, display;
        }

        private bool IsReferenceTargetVisible(TexturePaintLogicalTarget target)
        {
            if (target == null) return false;
            foreach (var member in target.members)
                if (visibleReferenceSlots.Contains(member.slotName)) return true;
            return false;
        }

        private void SetReferenceTargetVisible(TexturePaintLogicalTarget target, bool visible)
        {
            if (target == null || target.id == selectedTargetId) return;
            var slots = new List<string>();
            target.ExpandSlotNames(slots);
            foreach (string slot in slots)
            {
                visibleReferenceSlots.Remove(slot);
                if (visible) visibleReferenceSlots.Add(slot);
            }
            SetDocumentDirtyFlags();
            RefreshWorkspaceView();
        }

        private void DrawReferenceTargetToggle(Rect rect, TexturePaintLogicalTarget target, bool selected)
        {
            bool visible = selected || IsReferenceTargetVisible(target);
            using (new EditorGUI.DisabledScope(selected))
            {
                bool next = GUI.Toggle(rect, visible, new GUIContent("Show",
                    selected ? "The current paint target is always visible."
                    : "Show this target for reference only. It will not receive paint."), EditorStyles.miniButton);
                if (!selected && next != visible) SetReferenceTargetVisible(target, next);
            }
        }

        private void SetAllReferenceTargetsVisible(bool visible)
        {
            visibleReferenceSlots.Clear();
            if (visible && controller?.LogicalTargets != null)
                foreach (var target in controller.LogicalTargets.Targets)
                {
                    if (target.id == selectedTargetId) continue;
                    var slots = new List<string>(); target.ExpandSlotNames(slots);
                    foreach (string slot in slots)
                        if (!visibleReferenceSlots.Contains(slot)) visibleReferenceSlots.Add(slot);
                }
            SetDocumentDirtyFlags();
            RefreshWorkspaceView();
        }

        internal static HashSet<string> VisiblePreviewSlots(IEnumerable<string> active, IEnumerable<string> references)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (active != null) result.UnionWith(active);
            if (references != null) result.UnionWith(references);
            return result;
        }

        internal static int[] VisiblePreviewTriangles(ReconstructedSurface surface, ISet<string> visibleSlots)
        {
            var triangles = surface.mesh.triangles;
            var result = new List<int>(triangles.Length);
            bool unnamedVisible = false;
            foreach (string slot in surface.slotNames)
                if (visibleSlots.Contains(slot)) { unnamedVisible = true; break; }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                string owner = surface.GetTriangleSlotName(i / 3);
                if (string.IsNullOrEmpty(owner) ? !unnamedVisible : !visibleSlots.Contains(owner)) continue;
                result.Add(triangles[i]); result.Add(triangles[i + 1]); result.Add(triangles[i + 2]);
            }
            return result.ToArray();
        }

        private void ApplyReferenceTargetVisibility(ReconstructedSurface surface, HashSet<string> visibleSlots)
        {
            var filter = surface.gameObject.GetComponent<MeshFilter>();
            if (filter == null || surface.mesh == null) return;
            // Only the renderer's mesh is filtered. Colliders, UV maps and authored geometry
            // remain unchanged, and paint raycasts still use selectedSlots exclusively.
            var names = new List<string>(visibleSlots); names.Sort(StringComparer.Ordinal);
            string signature = string.Join("\n", names);
            if (!referenceDisplayMeshes.TryGetValue(surface, out var entry))
                referenceDisplayMeshes[surface] = entry = new ReferenceDisplayMesh();
            if (entry.signature == signature && entry.source == surface.mesh) return;
            entry.signature = signature; entry.source = surface.mesh;
            if (entry.display != null) DestroyImmediate(entry.display);
            entry.display = null;
            int[] triangles = VisiblePreviewTriangles(surface, visibleSlots);
            bool visible = triangles.Length > 0;
            surface.gameObject.SetActive(visible);
            if (visible && triangles.Length != surface.mesh.triangles.Length)
            {
                entry.display = Instantiate(surface.mesh);
                entry.display.name = surface.mesh.name + " (Reference Visibility)";
                entry.display.hideFlags = HideFlags.HideAndDontSave;
                entry.display.triangles = triangles;
            }
            filter.sharedMesh = entry.display != null ? entry.display : surface.mesh;
        }

        private void DisposeReferenceTargetVisibility()
        {
            foreach (var pair in referenceDisplayMeshes)
            {
                var filter = pair.Key.gameObject != null ? pair.Key.gameObject.GetComponent<MeshFilter>() : null;
                if (filter != null) filter.sharedMesh = pair.Key.mesh;
                if (pair.Value.display != null) DestroyImmediate(pair.Value.display);
            }
            referenceDisplayMeshes.Clear();
        }
    }
}
