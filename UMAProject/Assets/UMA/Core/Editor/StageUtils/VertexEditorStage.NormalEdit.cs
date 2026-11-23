using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Normals in Sculpt: show the editable slots' vertex normals, recalculate them from the current
    /// shape, reset them, and change them with the Comb Normals and Smooth Normals brushes. None of
    /// these moves a vertex.
    /// </summary>
    /// <remarks>
    /// The changes are on the stage's mesh, like a sculpt, and are saved the same ways: Save
    /// MeshModifier adds a Vertex Normal modifier per slot, and the slot saves (More Save Options)
    /// write them into the slot's MeshData. Shape strokes turn the normals with the faces that moved
    /// (VertexEditorStage.ShapeNormals), so changed normals survive later sculpting. Tangents are
    /// kept perpendicular to the changed normals.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private const float CombDegreesPerRadius = 60f;
        private const float MaxCombDegrees = 80f;
        private const float MinNormalLength = 0.001f;
        private const float MaxNormalLength = 0.05f;

        // Remembered with the other tool choices (VertexEditorToolSettings).
        private bool showSculptNormals;
        private float sculptNormalLength = 0.01f;

        [SerializeField] private bool showSculptNormalsSection = true;
        [NonSerialized] private string sculptNormalStatusMessage = string.Empty;

        private bool sculptStrokeIsNormals;
        private bool sculptNormalStrokeChanged;
        private Vector3[] sculptNormalStrokeNormals;
        private Vector4[] sculptNormalStrokeTangents;
        private Vector3[] sculptNormalStrokeStartNormals;
        private Vector3[] sculptNormalStrokePositions;

        private Vector3[] normalLineBuffer = Array.Empty<Vector3>();
        private Vector3[] editedNormalLineBuffer = Array.Empty<Vector3>();
        private readonly List<Vector3> normalDrawVertices = new List<Vector3>();
        private readonly List<Vector3> normalDrawNormals = new List<Vector3>();
        private readonly List<Vector3> normalLines = new List<Vector3>();
        private readonly List<Vector3> editedNormalLines = new List<Vector3>();

        private static readonly SculptToolInfo[] SculptNormalInfos =
        {
            new SculptToolInfo(SculptTool.CombNormals, "CombNormals", "Comb Normals", "Drag across the surface to tilt the normals under the brush toward the stroke. Vertices do not move."),
            new SculptToolInfo(SculptTool.SmoothNormals, "SmoothNormals", "Smooth Normals", "Even out the normals under the brush with their neighbors, across UV seams. Vertices do not move."),
        };

        private bool IsSculptNormalTool =>
            sculptMaskTool == SculptMaskTool.None &&
            (sculptTool == SculptTool.CombNormals || sculptTool == SculptTool.SmoothNormals);

        // ---- Strokes --------------------------------------------------------

        private void BeginSculptNormalStroke()
        {
            sculptNormalStrokeChanged = false;
            sculptStrokeIsNormals = IsSculptNormalTool && BakedMesh != null;
            if (!sculptStrokeIsNormals)
            {
                sculptNormalStrokeNormals = null;
                sculptNormalStrokeTangents = null;
                sculptNormalStrokeStartNormals = null;
                sculptNormalStrokePositions = null;
                return;
            }
            sculptNormalStrokeNormals = BakedMesh.normals;
            sculptNormalStrokeTangents = BakedMesh.tangents;
            sculptNormalStrokeStartNormals = (Vector3[])sculptNormalStrokeNormals.Clone();
            sculptNormalStrokePositions = BakedMesh.vertices;
            PrepareSculptFacingNormals();
            if (activeSculptState != null)
            {
                activeSculptState.normalsEdited = true;
            }
        }

        /// <summary>Ends a normals stroke. Returns false when the stroke was not one.</summary>
        private bool EndSculptNormalStroke(bool finalize)
        {
            if (!sculptStrokeIsNormals)
            {
                return false;
            }
            CommitSculptNormalSamples();
            sculptStrokeIsNormals = false;
            sculptNormalStrokeNormals = null;
            sculptNormalStrokeTangents = null;
            sculptNormalStrokeStartNormals = null;
            sculptNormalStrokePositions = null;
            sculptChangesUiDirty = true;
            if (finalize)
            {
                RepaintLinkedEditors();
                SceneView.RepaintAll();
            }
            return true;
        }

        private void CommitSculptNormalSamples()
        {
            if (!sculptNormalStrokeChanged || BakedMesh == null || sculptNormalStrokeNormals == null ||
                sculptNormalStrokeNormals.Length != BakedMesh.vertexCount)
            {
                return;
            }
            sculptNormalStrokeChanged = false;
            BakedMesh.normals = sculptNormalStrokeNormals;
            if (sculptNormalStrokeTangents != null && sculptNormalStrokeTangents.Length == BakedMesh.vertexCount)
            {
                BakedMesh.tangents = sculptNormalStrokeTangents;
            }
            RefreshBakedMeshCaches();
            sculptChangesUiDirty = true;
            EditorUtility.SetDirty(BakedMesh);
            EditorUtility.SetDirty(this);
        }

        /// <summary>One sample of Comb Normals or Smooth Normals, with its X Symmetry twin.</summary>
        private void ApplySculptNormalSample(Vector3 localPoint, Vector3 localNormal)
        {
            if (sculptNormalStrokeNormals == null || sculptNormalStrokePositions == null || sculptSlotStart < 0 ||
                sculptSlotStart + sculptSlotVertexCount > sculptNormalStrokeNormals.Length || sculptRadius <= 0f)
            {
                return;
            }
            Vector3 drag = sculptHasLastSample && VertexObject != null
                ? localPoint - VertexObject.transform.InverseTransformPoint(sculptLastSamplePoint)
                : Vector3.zero;
            int component = sculptConnectedOnly ? sculptHoverConnectedComponent : -1;
            ApplySculptNormalPass(localPoint, localNormal, drag, component);
            if (sculptSymmetryX)
            {
                Vector3 mirroredPoint = ReflectLocalX(localPoint);
                if ((mirroredPoint - localPoint).sqrMagnitude > 0.0000000001f)
                {
                    int mirroredComponent = sculptConnectedOnly
                        ? FindSculptConnectedComponentAtPoint(mirroredPoint, sculptNormalStrokePositions)
                        : -1;
                    ApplySculptNormalPass(mirroredPoint, ReflectLocalX(localNormal).normalized, ReflectLocalX(drag), mirroredComponent);
                }
            }
        }

        private void ApplySculptNormalPass(Vector3 point, Vector3 brushNormal, Vector3 drag, int connectedComponent)
        {
            if (sculptConnectedOnly && connectedComponent < 0)
            {
                return;
            }
            bool comb = sculptTool == SculptTool.CombNormals;
            float dragAmount = drag.magnitude / sculptRadius;
            if (comb && dragAmount <= 1e-6f)
            {
                // Combing needs a direction; the first sample of a stroke has none.
                return;
            }
            float strength = sculptStrengthPercent * 0.01f;
            Vector3[] normals = sculptNormalStrokeNormals;
            Vector3[] positions = sculptNormalStrokePositions;
            for (int i = 0; i < sculptSlotVertexCount; i++)
            {
                if (sculptConnectedOnly &&
                    (sculptConnectedComponents == null || sculptConnectedComponents[i] != connectedComponent))
                {
                    continue;
                }
                int baked = sculptSlotStart + i;
                float distance = Vector3.Distance(positions[baked], point);
                // Normals facing away from the brush are left alone, Front Faces Only or not: they
                // belong to the other side, such as the reversed copy of double-sided geometry.
                if (distance > sculptRadius || Vector3.Dot(normals[baked], brushNormal) <= 0f)
                {
                    continue;
                }
                float weight = EvaluateSculptFalloff(distance / sculptRadius) * strength;
                if (sculptMask != null && i < sculptMask.Length)
                {
                    weight *= 1f - sculptMask[i];
                }
                if (weight <= 0f)
                {
                    continue;
                }
                Vector3 normal = normals[baked];
                Vector3 changed;
                if (comb)
                {
                    Vector3 along = Vector3.ProjectOnPlane(drag, normal);
                    if (along.sqrMagnitude < 1e-14f)
                    {
                        continue;
                    }
                    changed = Vector3.RotateTowards(normal, along.normalized, CombDegreesPerRadius * Mathf.Deg2Rad * weight * dragAmount, 0f);
                    // A stroke tilts a normal at most MaxCombDegrees from where it found it.
                    Vector3 start = sculptNormalStrokeStartNormals[baked];
                    if (Vector3.Angle(changed, start) > MaxCombDegrees)
                    {
                        changed = Vector3.RotateTowards(start, changed, MaxCombDegrees * Mathf.Deg2Rad, 0f);
                    }
                }
                else
                {
                    Vector3 average = AverageNeighborNormal(i, normals, normal);
                    changed = Vector3.Slerp(normal, average, Mathf.Clamp01(weight * 0.5f));
                }
                if (changed.sqrMagnitude < 1e-12f)
                {
                    continue;
                }
                changed.Normalize();
                normals[baked] = changed;
                if (sculptNormalStrokeTangents != null && baked < sculptNormalStrokeTangents.Length)
                {
                    sculptNormalStrokeTangents[baked] = OrthogonalTangent(sculptNormalStrokeTangents[baked], changed);
                }
                sculptNormalStrokeChanged = true;
            }
        }

        /// <summary>
        /// The normals around a vertex of the active slot, counting welded copies (UV seams) as one
        /// vertex. Normals facing the other way (the reversed side of double-sided geometry) are left out.
        /// </summary>
        private Vector3 AverageNeighborNormal(int localIndex, Vector3[] normals, Vector3 own)
        {
            Vector3 sum = own;
            List<int> group = sculptCoincidentVertices != null && sculptCoincidentVertices[localIndex] != null
                ? sculptCoincidentVertices[localIndex]
                : null;
            int groupCount = group != null ? group.Count : 1;
            for (int g = 0; g < groupCount; g++)
            {
                int member = group != null ? group[g] : localIndex;
                if (member != localIndex)
                {
                    AddSameSide(normals[sculptSlotStart + member]);
                }
                if (sculptNeighbors == null || sculptNeighbors[member] == null)
                {
                    continue;
                }
                foreach (int neighbor in sculptNeighbors[member])
                {
                    AddSameSide(normals[sculptSlotStart + neighbor]);
                }
            }
            return sum.sqrMagnitude > 1e-12f ? sum.normalized : own;

            void AddSameSide(Vector3 normal)
            {
                if (Vector3.Dot(normal, own) > 0f) sum += normal;
            }
        }

        /// <summary>A tangent made perpendicular to <paramref name="normal"/>, keeping its handedness (w).</summary>
        internal static Vector4 OrthogonalTangent(Vector4 tangent, Vector3 normal)
        {
            Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z);
            Vector3 perpendicular = direction - (normal * Vector3.Dot(normal, direction));
            if (perpendicular.sqrMagnitude < 1e-12f)
            {
                perpendicular = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right);
            }
            perpendicular.Normalize();
            float handedness = tangent.w < 0f ? -1f : 1f;
            return new Vector4(perpendicular.x, perpendicular.y, perpendicular.z, handedness);
        }

        // ---- Whole-slot operations ------------------------------------------

        /// <summary>
        /// Recalculates the editable slots' normals from their current shape. Vertices at one position
        /// (UV seams, slot connections) get one normal; a vertex whose buddy is in a slot that is not
        /// editable takes that buddy's normal. One Undo step. Returns how many slots changed.
        /// </summary>
        public int RecalculateEditableSlotNormals()
        {
            EndSculptStroke(true);
            if (BakedMesh == null)
            {
                return 0;
            }
            // The slot saves work on the sculpt session's slot, and this can be called from a workflow
            // that has not started one (the Mesh Modifier editor's Recalculate Normals).
            EnsureSculptSession();
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, "Recalculate Normals");
            Vector3[] normals = BakedMesh.normals;
            Vector4[] tangents = BakedMesh.tangents;
            // Created before the mesh's normals change, so each keeps the slot's own normals.
            var states = new List<SculptSlotEditState>();
            foreach (SlotData slot in GetEditableSlots())
            {
                SculptSlotEditState state = GetOrCreateSculptSlotState(slot);
                if (state != null)
                {
                    states.Add(state);
                }
            }
            if (states.Count > 0)
            {
                GetBuddyNormals().Recalculate(BakedMesh.vertices, normals, false, BuildNormalLocks());
                foreach (SculptSlotEditState state in states)
                {
                    if (tangents != null && tangents.Length == normals.Length)
                    {
                        for (int i = state.start; i < state.start + state.count; i++)
                        {
                            tangents[i] = OrthogonalTangent(tangents[i], normals[i]);
                        }
                    }
                    state.normalsEdited = true;
                }
            }
            ApplyEditedNormals(normals, tangents);
            sculptNormalStatusMessage = states.Count == 0
                ? "No editable slot to recalculate."
                : "Recalculated the normals of " + states.Count + (states.Count == 1 ? " slot." : " slots.");
            return states.Count;
        }

        /// <summary>Puts the editable slots' normals back to the slot's own. One Undo step. Returns how many slots changed.</summary>
        public int ResetEditableSlotNormals()
        {
            EndSculptStroke(true);
            if (BakedMesh == null)
            {
                return 0;
            }
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, "Reset Normals");
            Vector3[] normals = BakedMesh.normals;
            Vector4[] tangents = BakedMesh.tangents;
            int changedSlots = 0;
            foreach (SlotData slot in GetEditableSlots())
            {
                if (!sculptSlotStates.TryGetValue(slot, out SculptSlotEditState state) || state == null ||
                    !state.hasOriginalNormals || state.originalNormals == null || state.start < 0 ||
                    state.start + state.count > normals.Length)
                {
                    continue;
                }
                Array.Copy(state.originalNormals, 0, normals, state.start, state.count);
                if (tangents != null && tangents.Length == normals.Length)
                {
                    for (int i = state.start; i < state.start + state.count; i++)
                    {
                        tangents[i] = OrthogonalTangent(tangents[i], normals[i]);
                    }
                }
                changedSlots++;
            }
            ApplyEditedNormals(normals, tangents);
            sculptNormalStatusMessage = changedSlots == 0
                ? "No editable slot has normals to reset."
                : "Reset the normals of " + changedSlots + (changedSlots == 1 ? " slot." : " slots.");
            return changedSlots;
        }

        private void ApplyEditedNormals(Vector3[] normals, Vector4[] tangents)
        {
            BakedMesh.normals = normals;
            if (tangents != null && tangents.Length == normals.Length)
            {
                BakedMesh.tangents = tangents;
            }
            RefreshBakedMeshCaches();
            sculptChangesUiDirty = true;
            EditorUtility.SetDirty(BakedMesh);
            EditorUtility.SetDirty(this);
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        // ---- Saving ---------------------------------------------------------

        /// <summary>
        /// A Vertex Normal modifier for a slot whose normals a normals tool changed, or whose tangents a
        /// UV tool turned: the new normal and tangent of each changed vertex, in the worn slot's mesh
        /// space. Null when nothing changed.
        /// </summary>
        private MeshModifier.Modifier CreateSculptNormalModifier(SculptSlotEditState state, bool useSlotSuffix)
        {
            if (state == null || state.slot == null || state.slot.asset == null || BakedMesh == null)
            {
                return null;
            }
            bool normalsEdited = state.normalsEdited && state.hasOriginalNormals && state.originalNormals != null;
            // A UV tool turns tangents with the texture; the normal modifier is what carries tangents.
            Vector4[] bakedTangents = state.uvEdited && state.originalTangents != null ? BakedMesh.tangents : null;
            if (!normalsEdited && bakedTangents == null)
            {
                return null;
            }
            UMAMeshData meshData = state.slot.asset.meshData;
            if (UMAMeshData.IsNullOrEmptyMeshData(meshData) || meshData.normals == null || meshData.normals.Length != state.count ||
                !EnsureBakedSkin(out _))
            {
                return null;
            }
            Vector3[] normals = BakedMesh.normals;
            if (state.start < 0 || state.start + state.count > normals.Length)
            {
                return null;
            }
            string slotKey = GetSculptSlotKey(state.slot);
            string baseName = string.IsNullOrWhiteSpace(sculptModifierName) ? "Sculpt" : sculptModifierName;
            var modifier = new MeshModifier.Modifier
            {
                ModifierName = (useSlotSuffix ? baseName + " - " + state.slot.slotName : baseName) + " Normals",
                SlotName = slotKey,
                Scale = 1f,
                DNAName = string.Empty,
                manuallyModified = true,
                adjustments = new VertexNormalAdjustmentCollection()
            };
            if (bakedTangents != null && state.start + state.count > bakedTangents.Length)
            {
                bakedTangents = null;
            }
            for (int i = 0; i < state.count; i++)
            {
                Vector3 delta = normalsEdited ? normals[state.start + i] - state.originalNormals[i] : Vector3.zero;
                bool normalChanged = delta.sqrMagnitude > 1e-10f;
                float turn = bakedTangents != null
                    ? TangentTurn(state.originalTangents[i], bakedTangents[state.start + i], normals[state.start + i])
                    : 0f;
                bool tangentTurned = Mathf.Abs(turn) >= TangentTurnThreshold;
                if (!normalChanged && !tangentTurned)
                {
                    continue;
                }
                Vector3 normal = meshData.normals[i];
                if (normalChanged)
                {
                    normal += BakedToWornSpace(state.start + i, delta);
                }
                if (normal.sqrMagnitude < 1e-12f)
                {
                    continue;
                }
                normal.Normalize();
                Vector4 tangent = meshData.tangents != null && i < meshData.tangents.Length
                    ? OrthogonalTangent(meshData.tangents[i], normal)
                    : OrthogonalTangent(Vector4.zero, normal);
                if (tangentTurned)
                {
                    tangent = TurnTangent(tangent, normal, turn);
                }
                modifier.adjustments.vertexAdjustments.Add(new VertexNormalAdjustment
                {
                    vertexIndex = i,
                    slotName = slotKey,
                    weight = 1f,
                    normal = normal,
                    tangent = new Vector3(tangent.x, tangent.y, tangent.z),
                    rotation = Quaternion.identity
                });
            }
            return modifier.adjustments.vertexAdjustments.Count > 0 ? modifier : null;
        }

        // ---- Display ----------------------------------------------------------

        /// <summary>Draws the editable slots' normals; ones a normals tool changed are orange.</summary>
        private void DrawSculptNormals()
        {
            if (!showSculptNormals || BakedMesh == null || VertexObject == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            BakedMesh.GetVertices(normalDrawVertices);
            BakedMesh.GetNormals(normalDrawNormals);
            if (normalDrawNormals.Count != normalDrawVertices.Count)
            {
                return;
            }
            List<SlotData> slots = GetEditableSlots();
            int total = 0;
            foreach (SlotData slot in slots) total += slot.asset != null && slot.asset.meshData != null ? slot.asset.meshData.vertexCount : 0;
            // On a large selection only the normals near the brush are drawn.
            bool nearBrushOnly = total > 20000;
            if (nearBrushOnly && !sculptHoverValid)
            {
                return;
            }
            Transform space = VertexObject.transform;
            Vector3 brushPoint = space.InverseTransformPoint(sculptHoverPoint);
            float nearSquared = (sculptRadius * 4f) * (sculptRadius * 4f);
            normalLines.Clear();
            editedNormalLines.Clear();
            foreach (SlotData slot in slots)
            {
                if (slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData)) continue;
                int start = GetVisibleBakedVertexIndex(slot, 0);
                int count = slot.asset.meshData.vertexCount;
                if (start < 0 || start + count > normalDrawVertices.Count) continue;
                sculptSlotStates.TryGetValue(slot, out SculptSlotEditState state);
                bool compare = state != null && state.normalsEdited && state.hasOriginalNormals && state.originalNormals != null;
                for (int i = 0; i < count; i++)
                {
                    Vector3 local = normalDrawVertices[start + i];
                    if (nearBrushOnly && (local - brushPoint).sqrMagnitude > nearSquared) continue;
                    Vector3 normal = normalDrawNormals[start + i];
                    Vector3 from = space.TransformPoint(local);
                    Vector3 to = from + (space.TransformDirection(normal).normalized * sculptNormalLength);
                    List<Vector3> target = compare && (normal - state.originalNormals[i]).sqrMagnitude > 1e-6f
                        ? editedNormalLines
                        : normalLines;
                    target.Add(from);
                    target.Add(to);
                }
            }
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Color previousColor = Handles.color;
            try
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                DrawNormalLines(normalLines, ref normalLineBuffer, new Color(0.25f, 0.8f, 1f, 0.9f));
                DrawNormalLines(editedNormalLines, ref editedNormalLineBuffer, new Color(1f, 0.55f, 0.1f, 1f));
            }
            finally
            {
                Handles.zTest = previousZTest;
                Handles.color = previousColor;
            }
        }

        private static void DrawNormalLines(List<Vector3> lines, ref Vector3[] buffer, Color color)
        {
            if (lines.Count < 2)
            {
                return;
            }
            if (buffer.Length != lines.Count)
            {
                buffer = new Vector3[lines.Count];
            }
            lines.CopyTo(buffer);
            Handles.color = color;
            Handles.DrawLines(buffer);
        }

        // ---- Panel ------------------------------------------------------------

        private void DrawSculptNormalOptions()
        {
            if (!DrawSectionFoldout(ref showSculptNormalsSection, "Normals"))
            {
                return;
            }
            GUILayout.Label(
                "Comb Normals and Smooth Normals (in the last group of brushes on the toolbar) change normals without moving vertices. " +
                "Shape strokes recalculate normals when they end, so change normals after the shape is done. " +
                "Save them from the Save panel of the Vertex Editor window (Mesh Modifier, or into the slot).",
                EditorStyles.wordWrappedMiniLabel);
            bool show = EditorGUILayout.Toggle(
                new GUIContent("Show Normals", "Draw the editable slots' vertex normals. Normals changed by a normals tool are orange."),
                showSculptNormals);
            if (show != showSculptNormals)
            {
                showSculptNormals = show;
                SceneView.RepaintAll();
            }
            using (new EditorGUI.DisabledScope(!showSculptNormals))
            {
                float length = StageBrushSize.MillimeterSlider(
                    new GUIContent("Length (mm)", "How long the drawn normals are."),
                    sculptNormalLength, MinNormalLength * 1000f, MaxNormalLength * 1000f);
                if (!Mathf.Approximately(length, sculptNormalLength))
                {
                    sculptNormalLength = length;
                    SceneView.RepaintAll();
                }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Recalculate", "Work out the editable slots' normals again from their current shape. Vertices at one position (UV seams, slot connections) get one normal. One Undo step.")))
            {
                RecalculateEditableSlotNormals();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(new GUIContent("Reset", "Put the editable slots' normals back to the slot's own. One Undo step.")))
            {
                ResetEditableSlotNormals();
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(sculptNormalStatusMessage))
            {
                EditorGUILayout.LabelField(sculptNormalStatusMessage, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
