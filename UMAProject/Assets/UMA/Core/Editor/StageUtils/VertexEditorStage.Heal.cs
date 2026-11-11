using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Heal: evens out how far the surface has been moved, instead of evening out the surface.
    /// </summary>
    /// <remarks>
    /// Every vertex has a movement: where it is now, less where it was in the base shape. While a
    /// blendshape is being edited the base is the character at rest, so the movement is the blendshape;
    /// otherwise the base is the mesh as it was when the editor opened, so the movement is the sculpt.
    /// Heal replaces a vertex's movement with that of its neighbours. Where the movement becomes even,
    /// the surface is the base shape again, carried along with its surroundings: a panel that was
    /// modelled flat and evenly spaced is flat and evenly spaced again. Smooth, by contrast, moves
    /// vertices to the average position of their neighbours, which shrinks and reshapes the surface.
    /// The Heal brush does this under the pointer; Heal Masked and Heal Unmasked solve a whole area at
    /// once from the movement at its edge.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private const int HealMaxIterations = 4000;
        private const float HealConvergence = 1e-7f;
        private const float HealWeldScale = 20000f; // welds positions within 0.05 mm
        private const float HealOverRelaxation = 1.6f;

        [NonSerialized] private string healStatusMessage = string.Empty;
        [NonSerialized] private MessageType healStatusType = MessageType.Info;

        /// <summary>The message of the last Heal Masked or Heal Unmasked.</summary>
        public string HealStatus => healStatusMessage;

        /// <summary>
        /// Base position of a vertex of the active sculpt slot, in baked mesh space: the character at
        /// rest while a blendshape is edited, otherwise the mesh as it was when the editor opened.
        /// </summary>
        private Vector3 HealBaseVertex(int localIndex)
        {
            int baked = sculptSlotStart + localIndex;
            if (IsEditingBlendshape && baked >= 0 && baked < editRestVertices.Length)
            {
                return editRestVertices[baked];
            }
            return sculptOriginalVertices[localIndex];
        }

        /// <summary>
        /// The Heal brush on one welded vertex: its movement goes towards the average movement of its
        /// neighbours by <paramref name="blend"/>.
        /// </summary>
        private Vector3 HealedSculptPosition(int localIndex, HashSet<int> neighbors, Vector3[] before, float blend)
        {
            Vector3 basePosition = HealBaseVertex(localIndex);
            Vector3 movement = before[sculptSlotStart + localIndex] - basePosition;
            Vector3 average = Vector3.zero;
            foreach (int neighbor in neighbors)
            {
                average += before[sculptSlotStart + neighbor] - HealBaseVertex(neighbor);
            }
            average /= neighbors.Count;
            return basePosition + Vector3.Lerp(movement, average, blend);
        }

        private void DrawHealOptions()
        {
            EditorGUILayout.HelpBox(
                (IsEditingBlendshape
                    ? "Evens out how far the blendshape moves the vertices under the brush, so the surface returns to its modelled shape and spacing, carried along with its surroundings. "
                    : "Evens out how far the sculpt has moved the vertices under the brush, so the surface returns to its original shape and spacing, carried along with its surroundings. ") +
                "It works outward from good areas: brush from them into the damage, or solve a whole area at once below.",
                MessageType.None);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Heal Masked", "Paint the mask over the damaged area, then press this. The masked vertices take their movement from the unmasked vertices around them, in one step. Works on every editable slot.")))
            {
                HealArea(true);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(new GUIContent("Heal Unmasked", "The opposite: everything that is not masked takes its movement from the masked vertices around it. Mask what must stay as it is first. Works on every editable slot.")))
            {
                HealArea(false);
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(healStatusMessage))
            {
                EditorGUILayout.HelpBox(healStatusMessage, healStatusType);
            }
        }

        /// <summary>
        /// Solves a whole area at once. With <paramref name="masked"/> the masked vertices are healed
        /// from the unmasked ones around them; without it the unmasked vertices are healed from the
        /// masked ones. A partly masked vertex is healed in proportion. Covers every editable slot;
        /// vertices that share a position, also across slots, stay together. One Undo step.
        /// </summary>
        /// <returns>The number of vertices that moved.</returns>
        public int HealArea(bool masked)
        {
            string label = masked ? "Heal Masked" : "Heal Unmasked";
            if (BakedMesh == null || sculpting)
            {
                return SetHealStatus(0, label + ": the mesh is not ready.", MessageType.Warning);
            }
            List<SlotData> slots = GetEditableSlots();
            int vertexCount = BakedMesh.vertexCount;
            Vector3[] vertices = BakedMesh.vertices;
            // Base positions: the rest shape while a blendshape is edited; elsewhere each editable
            // slot's original vertices. Everything else has not moved.
            var basePositions = (Vector3[])vertices.Clone();
            bool blendshape = IsEditingBlendshape && editRestVertices.Length == vertexCount;
            if (blendshape)
            {
                Array.Copy(editRestVertices, basePositions, vertexCount);
            }
            // How much of the solved movement each vertex takes: 0 leaves it alone and makes it an anchor.
            var take = new float[vertexCount];
            var editable = new bool[vertexCount];
            int slotCount = 0;
            bool anyMasked = false;
            foreach (SlotData slot in slots)
            {
                SculptSlotEditState state = GetOrCreateSculptSlotState(slot);
                if (state == null || state.start < 0 || state.start + state.count > vertexCount)
                {
                    continue;
                }
                slotCount++;
                float[] mask = GetSculptStateMask(state);
                for (int i = 0; i < state.count; i++)
                {
                    int baked = state.start + i;
                    editable[baked] = true;
                    float m = mask != null && i < mask.Length ? Mathf.Clamp01(mask[i]) : 0f;
                    take[baked] = masked ? m : 1f - m;
                    if (m > 0.5f) anyMasked = true;
                    if (!blendshape && state.originalVertices != null && i < state.originalVertices.Length)
                    {
                        basePositions[baked] = state.originalVertices[i];
                    }
                }
            }
            if (slotCount == 0)
            {
                return SetHealStatus(0, label + ": no slot is editable.", MessageType.Warning);
            }
            if (!masked && !anyMasked)
            {
                // Without a mask the whole of every editable slot would be evened out from whatever
                // few points it shares with a read-only slot, which flattens the shape.
                return SetHealStatus(0, "Heal Unmasked: nothing is masked. Mask the vertices that must stay as they are first, or paint the mask over the damage and use Heal Masked.", MessageType.Warning);
            }

            // Vertices at one base position are one point of the surface: UV seams, and the seams between slots.
            var groupOf = new int[vertexCount];
            var groupByPosition = new Dictionary<Vector3Int, int>(vertexCount);
            int groupCount = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 p = basePositions[i] * HealWeldScale;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!groupByPosition.TryGetValue(key, out int group))
                {
                    group = groupCount++;
                    groupByPosition.Add(key, group);
                }
                groupOf[i] = group;
            }
            var groupTake = new float[groupCount];
            var groupLocked = new bool[groupCount];
            var groupMovement = new Vector3[groupCount];
            var groupMembers = new int[groupCount];
            for (int i = 0; i < vertexCount; i++)
            {
                int group = groupOf[i];
                // A point shared with a read-only slot stays where it is, so the seam stays closed.
                if (!editable[i]) groupLocked[group] = true;
                groupTake[group] = Mathf.Max(groupTake[group], take[i]);
                groupMovement[group] += vertices[i] - basePositions[i];
                groupMembers[group]++;
            }
            int freeCount = 0;
            for (int g = 0; g < groupCount; g++)
            {
                groupMovement[g] /= Mathf.Max(1, groupMembers[g]);
                if (groupLocked[g]) groupTake[g] = 0f;
                if (groupTake[g] > 0.001f) freeCount++;
            }
            if (freeCount == 0)
            {
                return SetHealStatus(0, masked
                    ? "Heal Masked: nothing is masked on an editable slot. Paint the mask over the area to heal first."
                    : "Heal Unmasked: everything editable is fully masked, so there is nothing to heal.", MessageType.Warning);
            }

            // Neighbours of each group, from the triangles.
            var neighbors = new HashSet<int>[groupCount];
            int[] triangles = GetBakedTriangles();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = groupOf[triangles[t]], b = groupOf[triangles[t + 1]], c = groupOf[triangles[t + 2]];
                LinkHealGroups(neighbors, groupTake, a, b);
                LinkHealGroups(neighbors, groupTake, b, c);
                LinkHealGroups(neighbors, groupTake, c, a);
            }

            // An area with no anchor anywhere around it has nothing to take its movement from.
            var anchored = new bool[groupCount];
            var queue = new Queue<int>();
            for (int g = 0; g < groupCount; g++)
            {
                if (groupTake[g] > 0.001f || neighbors[g] == null) continue;
                // An anchor that touches the area.
                anchored[g] = true;
                queue.Enqueue(g);
            }
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                if (neighbors[g] == null) continue;
                foreach (int n in neighbors[g])
                {
                    if (anchored[n] || groupTake[n] <= 0.001f) continue;
                    anchored[n] = true;
                    queue.Enqueue(n);
                }
            }
            var free = new List<int>(freeCount);
            int stranded = 0;
            for (int g = 0; g < groupCount; g++)
            {
                if (groupTake[g] <= 0.001f) continue;
                if (anchored[g]) free.Add(g);
                else stranded++;
            }
            if (free.Count == 0)
            {
                return SetHealStatus(0, masked
                    ? "Heal Masked: the masked area has no unmasked vertices around it to take its movement from. Leave part of each slot unmasked."
                    : "Heal Unmasked: nothing is masked around the area, so there is no movement to take. Mask the vertices that must stay as they are first.", MessageType.Warning);
            }

            // Each free point takes the average movement of its neighbours, until nothing changes.
            var links = new int[free.Count][];
            for (int f = 0; f < free.Count; f++)
            {
                var list = new List<int>(neighbors[free[f]].Count);
                foreach (int n in neighbors[free[f]])
                {
                    // A stranded neighbour has no say: its own movement is not being solved.
                    if (groupTake[n] <= 0.001f || anchored[n]) list.Add(n);
                }
                links[f] = list.ToArray();
            }
            var solved = (Vector3[])groupMovement.Clone();
            int iterations = 0;
            try
            {
                for (; iterations < HealMaxIterations; iterations++)
                {
                    float largest = 0f;
                    for (int f = 0; f < free.Count; f++)
                    {
                        int[] around = links[f];
                        if (around.Length == 0) continue;
                        int g = free[f];
                        Vector3 sum = Vector3.zero;
                        for (int n = 0; n < around.Length; n++) sum += solved[around[n]];
                        // Stepping a little past the average reaches the answer in far fewer rounds.
                        Vector3 next = solved[g] + HealOverRelaxation * (sum / around.Length - solved[g]);
                        largest = Mathf.Max(largest, (next - solved[g]).sqrMagnitude);
                        solved[g] = next;
                    }
                    if (largest < HealConvergence * HealConvergence) break;
                    if ((iterations & 127) == 127 &&
                        EditorUtility.DisplayCancelableProgressBar(label, "Evening out the movement of " + free.Count + " points", iterations / (float)HealMaxIterations))
                    {
                        break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            int moved = 0;
            float furthest = 0f;
            for (int i = 0; i < vertexCount; i++)
            {
                int g = groupOf[i];
                if (!editable[i] || groupTake[g] <= 0.001f || !anchored[g]) continue;
                // The whole point moves together, by the largest share any of its vertices asked for.
                Vector3 movement = Vector3.Lerp(groupMovement[g], solved[g], groupTake[g]);
                Vector3 position = basePositions[i] + movement;
                float distance = (position - vertices[i]).magnitude;
                if (distance <= 1e-7f) continue;
                vertices[i] = position;
                moved++;
                furthest = Mathf.Max(furthest, distance);
            }
            if (moved > 0)
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(label);
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, label);
                BakedMesh.vertices = vertices;
                BakedMesh.RecalculateNormals();
                SynchronizeCrossSlotSculptNormals();
                BakedMesh.RecalculateBounds();
                RefreshBakedMeshCaches();
                RefreshSculptCollider();
                sculptHoverValid = false;
                sculptChangesUiDirty = true;
                EditorUtility.SetDirty(BakedMesh);
                EditorUtility.SetDirty(this);
                Undo.CollapseUndoOperations(undoGroup);
            }
            RepaintLinkedEditors();
            SceneView.RepaintAll();
            string message = moved > 0
                ? $"{label} moved {moved} vertices on {slotCount} slot(s), by up to {furthest * 1000f:0.0} mm."
                : $"{label}: the area already moves evenly; nothing changed.";
            if (stranded > 0)
            {
                message += $" {stranded} point(s) were left alone: nothing around them to take a movement from.";
            }
            if (iterations >= HealMaxIterations)
            {
                message += " The area is large and was not fully evened out; press again to continue.";
            }
            return SetHealStatus(moved, message, stranded > 0 || iterations >= HealMaxIterations ? MessageType.Warning : MessageType.Info);
        }

        // Links two points of the surface when at least one of them is being healed.
        private static void LinkHealGroups(HashSet<int>[] neighbors, float[] groupTake, int a, int b)
        {
            if (a == b || (groupTake[a] <= 0.001f && groupTake[b] <= 0.001f))
            {
                return;
            }
            (neighbors[a] ?? (neighbors[a] = new HashSet<int>())).Add(b);
            (neighbors[b] ?? (neighbors[b] = new HashSet<int>())).Add(a);
        }

        private int SetHealStatus(int moved, string message, MessageType type)
        {
            healStatusMessage = message ?? string.Empty;
            healStatusType = type;
            RepaintLinkedEditors();
            return moved;
        }
    }
}
