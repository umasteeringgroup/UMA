using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// UV Sculpt: drag the texture across the surface. No vertex moves; the UVs of the vertices under
    /// the brush change, so the texture follows the pointer and fades out by the brush's falloff.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stroke is anchored like Grab: the brush stays where the button went down, and the drag is
    /// measured on the Scene camera's view plane. The texture at a point moves with the drag by the
    /// brush's weight at that point, so the texture under the pointer stays under the pointer (at
    /// Effect 100). A vertex therefore shows the texture from the point that ended up on it, which is
    /// worked out per vertex for every movement of the pointer. Each UV island under the brush moves in
    /// one direction in the texture, so it stays smooth. For the island under the pointer that is
    /// the difference between the UV where the brush was put down and the UV where the pointer is
    /// now, read from the mesh, so its texture arrives exactly. An island on the other side of a UV
    /// seam moves by its own layout: the same movement across the surface, turned into a change of UV
    /// with that island's surface gradients of u and v. Copies of a vertex that share a UV keep
    /// sharing it. The texture stops where a triangle would fold over itself in the
    /// texture, and it cannot be carried out of the brush: a drag longer than the radius bunches it up
    /// against the brush's edge.
    /// </para>
    /// <para>
    /// Tangents are turned with the texture when the stroke ends, so normal maps still shade the right
    /// way. The changes are on the stage's mesh, like a sculpt, and are saved the same ways: Save as
    /// Mesh Modifier adds a Vertex UV modifier per slot, and the slot saves write them into the slot's
    /// MeshData. The stage's mesh has the atlas mapping in its UVs; a save takes that back out.
    /// </para>
    /// </remarks>
    public partial class VertexEditorStage
    {
        /// <summary>A UV triangle may shrink to this share of the area it had when the stroke began, and no further.</summary>
        private const float UVMinAreaRatio = 0.1f;
        private const float UVChangedSqr = 1e-14f;
        private const float TangentTurnThreshold = 0.05f; // degrees

        // Remembered with the other tool choices (VertexEditorToolSettings).
        private bool sculptUVLockBorders;

        [NonSerialized] private string sculptUVStatusMessage = string.Empty;

        private static readonly SculptToolInfo[] SculptUVInfos =
        {
            new SculptToolInfo(SculptTool.UVSculpt, "UVSculpt", "UV Sculpt", "Drag the texture across the surface. The UVs under the brush change; vertices do not move."),
        };

        private bool IsSculptUVTool => sculptMaskTool == SculptMaskTool.None && sculptTool == SculptTool.UVSculpt;

        private bool sculptStrokeIsUV;
        private bool uvStrokeChanged;
        private bool uvStrokeLimited;
        private Vector2[] uvStrokeStart;
        private Vector2[] uvStrokeUVs;
        private Vector2[] uvStrokeScratch;
        private Vector3[] uvStrokePositions;
        private int[] uvStrokeVertices;
        // The most a vertex can follow the drag (Effect and mask), and where the brush was put down.
        private float[] uvStrokeMaxWeights;
        private float[] uvStrokeMirroredMaxWeights;
        private Vector3 uvStrokeCentre;
        private Vector3 uvStrokeMirroredCentre;
        // The surface under the brush and the way the camera looks, in the mesh's space.
        private Vector3 uvStrokeNormal;
        private Vector3 uvStrokeViewDirection;
        private Vector3[] uvStrokeGradU;
        private Vector3[] uvStrokeGradV;
        private Vector3[] uvStrokeMirroredGradU;
        private Vector3[] uvStrokeMirroredGradV;
        private Vector2[] uvStrokeOffsets;
        // The island under the pointer (and under its mirror image): its triangles under the brush,
        // which of the stroke's vertices are in it, and the UV where the brush was put down.
        private int[] uvStrokeCentreTriangles;
        private bool[] uvStrokeInCentreIsland;
        private Vector2 uvStrokeCentreUV;
        private int[] uvStrokeMirroredCentreTriangles;
        private bool[] uvStrokeInMirroredCentreIsland;
        private Vector2 uvStrokeMirroredCentreUV;
        private int[] uvStrokeTriangles;
        private float[] uvStrokeTriangleAreas;
        private readonly List<SculptSlotEditState> uvStrokeStates = new List<SculptSlotEditState>();

        private readonly Dictionary<SculptSlotEditState, SculptUVTopology> uvTopologies = new Dictionary<SculptSlotEditState, SculptUVTopology>();
        private Mesh uvTopologyMesh;

        private readonly List<Vector2> sculptChangeUVs = new List<Vector2>();

        // ---- Geometry -----------------------------------------------------------

        /// <summary>How a slot's vertices hang together in the texture.</summary>
        internal sealed class SculptUVTopology
        {
            /// <summary>Per vertex: the first vertex with the same position and UV. Such copies (hard edges) are one point of the texture.</summary>
            public int[] weld;
            /// <summary>Per vertex: on the edge of its UV island, or on an open edge of the mesh.</summary>
            public bool[] border;
            /// <summary>Per vertex: its UV island.</summary>
            public int[] island;
        }

        /// <summary>
        /// The surface gradients of u and v on a triangle: a movement <c>d</c> across the triangle changes
        /// the UV by (Dot(gradU, d), Dot(gradV, d)). False for a triangle without area.
        /// </summary>
        internal static bool TriangleUVGradients(
            Vector3 p0, Vector3 p1, Vector3 p2, Vector2 uv0, Vector2 uv1, Vector2 uv2,
            out Vector3 gradU, out Vector3 gradV, out float area)
        {
            Vector3 e1 = p1 - p0;
            Vector3 e2 = p2 - p0;
            float g11 = Vector3.Dot(e1, e1), g12 = Vector3.Dot(e1, e2), g22 = Vector3.Dot(e2, e2);
            float determinant = (g11 * g22) - (g12 * g12);
            gradU = Vector3.zero;
            gradV = Vector3.zero;
            area = 0.5f * Mathf.Sqrt(Mathf.Max(determinant, 0f));
            if (determinant <= 1e-20f)
            {
                return false;
            }
            // d1 and d2 pick the e1 and e2 parts out of a movement in the triangle's plane.
            Vector3 d1 = ((e1 * g22) - (e2 * g12)) / determinant;
            Vector3 d2 = ((e2 * g11) - (e1 * g12)) / determinant;
            Vector2 a = uv1 - uv0;
            Vector2 b = uv2 - uv0;
            gradU = (d1 * a.x) + (d2 * b.x);
            gradV = (d1 * a.y) + (d2 * b.y);
            return true;
        }

        /// <summary>The direction on a triangle in which u grows while v stays, scaled by the triangle's area. Zero when the triangle has no area in the texture.</summary>
        internal static Vector3 TriangleTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            Vector3 e1 = p1 - p0;
            Vector3 e2 = p2 - p0;
            Vector2 a = uv1 - uv0;
            Vector2 b = uv2 - uv0;
            float determinant = (a.x * b.y) - (b.x * a.y);
            if (Mathf.Abs(determinant) < 1e-20f)
            {
                return Vector3.zero;
            }
            Vector3 tangent = ((e1 * b.y) - (e2 * a.y)) / determinant;
            float length = tangent.magnitude;
            return length > 1e-12f ? tangent * (0.5f * Vector3.Cross(e1, e2).magnitude / length) : Vector3.zero;
        }

        internal static float SignedUVArea(Vector2 a, Vector2 b, Vector2 c)
        {
            return 0.5f * (((b.x - a.x) * (c.y - a.y)) - ((c.x - a.x) * (b.y - a.y)));
        }

        /// <summary>
        /// Works out which of a slot's vertices are one point of the texture, where its UV islands end,
        /// and which island each vertex is in. The slot's vertices are [start, start + count) of
        /// <paramref name="positions"/> and <paramref name="uvs"/>; <paramref name="slotTriangles"/> are
        /// the first indices of its triangles in <paramref name="triangles"/>.
        /// </summary>
        internal static SculptUVTopology BuildSculptUVTopology(
            int start, int count, int[] slotTriangles, int[] triangles, Vector3[] positions, Vector2[] uvs,
            float positionTolerance, float uvTolerance)
        {
            var topology = new SculptUVTopology { weld = new int[count], border = new bool[count], island = new int[count] };
            float inversePosition = 1f / Mathf.Max(positionTolerance, 1e-9f);
            float inverseUV = 1f / Mathf.Max(uvTolerance, 1e-9f);
            var groups = new Dictionary<(Vector3Int, Vector2Int), int>(count);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = positions[start + i] * inversePosition;
                Vector2 uv = uvs[start + i] * inverseUV;
                var key = (
                    new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z)),
                    new Vector2Int(Mathf.RoundToInt(uv.x), Mathf.RoundToInt(uv.y)));
                if (!groups.TryGetValue(key, out int first))
                {
                    first = i;
                    groups.Add(key, i);
                }
                topology.weld[i] = first;
            }

            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }
            var edgeUses = new Dictionary<long, int>();
            void CountEdge(int x, int y)
            {
                if (x == y) return;
                long key = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
                edgeUses.TryGetValue(key, out int uses);
                edgeUses[key] = uses + 1;
                int rootX = Find(x), rootY = Find(y);
                if (rootX != rootY) parent[rootX] = rootY;
            }
            if (slotTriangles != null)
            {
                foreach (int first in slotTriangles)
                {
                    int a = triangles[first] - start, b = triangles[first + 1] - start, c = triangles[first + 2] - start;
                    if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count)
                    {
                        continue;
                    }
                    a = topology.weld[a];
                    b = topology.weld[b];
                    c = topology.weld[c];
                    CountEdge(a, b);
                    CountEdge(b, c);
                    CountEdge(c, a);
                }
            }
            foreach (KeyValuePair<long, int> edge in edgeUses)
            {
                if (edge.Value == 1)
                {
                    topology.border[(int)(edge.Key >> 32)] = true;
                    topology.border[(int)(edge.Key & 0xFFFFFFFFL)] = true;
                }
            }
            for (int i = 0; i < count; i++)
            {
                topology.border[i] = topology.border[topology.weld[i]];
                topology.island[i] = Find(topology.weld[i]);
            }
            return topology;
        }

        /// <summary>
        /// The movement across the surface that keeps a point under the pointer. The pointer's drag is
        /// measured on the camera's view plane; on a surface turned away from the camera the point has
        /// to travel further along the surface to stay under it. Seen nearly edge on (within about 12
        /// degrees) that distance runs away, and the drag is laid flat onto the surface instead.
        /// </summary>
        internal static Vector3 DragOnSurface(Vector3 drag, Vector3 normal, Vector3 viewDirection)
        {
            if (normal.sqrMagnitude < 1e-12f)
            {
                return drag;
            }
            normal.Normalize();
            float facing = viewDirection.sqrMagnitude > 1e-12f ? Vector3.Dot(normal, viewDirection.normalized) : 0f;
            if (Mathf.Abs(facing) < 0.2f)
            {
                return Vector3.ProjectOnPlane(drag, normal);
            }
            // Slide along the line of sight until the movement lies in the surface.
            return drag - (viewDirection.normalized * (Vector3.Dot(normal, drag) / facing));
        }

        /// <summary>
        /// A surface gradient turned so that it can be used with the pointer's drag on the view plane:
        /// Dot(result, drag) is Dot(gradient, DragOnSurface(drag, normal, viewDirection)).
        /// </summary>
        internal static Vector3 ViewCorrectedGradient(Vector3 gradient, Vector3 normal, Vector3 viewDirection)
        {
            if (normal.sqrMagnitude < 1e-12f)
            {
                return gradient;
            }
            normal.Normalize();
            float facing = viewDirection.sqrMagnitude > 1e-12f ? Vector3.Dot(normal, viewDirection.normalized) : 0f;
            if (Mathf.Abs(facing) < 0.2f)
            {
                return gradient - (normal * Vector3.Dot(gradient, normal));
            }
            return gradient - (normal * (Vector3.Dot(gradient, viewDirection.normalized) / facing));
        }

        /// <summary>
        /// How much a vertex counts when an island's gradients are averaged: mostly the vertices near
        /// the middle of the brush, so the texture under the pointer is the texture that follows it.
        /// </summary>
        internal static float GradientAveragingWeight(float normalizedDistance)
        {
            float inside = 1f - (normalizedDistance * normalizedDistance);
            if (inside <= 0f)
            {
                return 0f;
            }
            inside *= inside;
            return inside * inside;
        }

        /// <summary>
        /// How much of a drag reaches a vertex. The texture at a point moves with the drag by the brush's
        /// weight there (<paramref name="maxWeight"/> times the falloff of its distance from
        /// <paramref name="centre"/>), so a vertex shows the texture from the point that ended up on it.
        /// That point's weight is the answer: weight = maxWeight * falloff(|position - weight * drag -
        /// centre| / radius). Nothing outside the radius moves.
        /// </summary>
        internal static float DraggedWeight(
            float maxWeight, Vector3 position, Vector3 centre, Vector3 drag, float radius, Func<float, float> falloff)
        {
            if (maxWeight <= 0f || radius <= 0f)
            {
                return 0f;
            }
            float WeightAtSource(float weight)
            {
                float distance = (position - (drag * weight) - centre).magnitude / radius;
                return distance > 1f ? 0f : maxWeight * Mathf.Clamp01(falloff(distance));
            }
            float low = 0f, high = maxWeight;
            if (WeightAtSource(high) >= high)
            {
                return high;
            }
            // The weight at the source is at least `low` and less than `high`: close in on where they meet.
            for (int i = 0; i < 14; i++)
            {
                float middle = (low + high) * 0.5f;
                if (WeightAtSource(middle) >= middle) low = middle;
                else high = middle;
            }
            // The lower end: exactly 0 for a vertex nothing reaches.
            return low;
        }

        /// <summary>
        /// How much of a UV drag can be applied before a triangle folds over in the texture: 1 for all
        /// of it. Each vertex in <paramref name="vertices"/> moves from its UV in <paramref name="startUVs"/>
        /// by minus its offset; no triangle may shrink below <paramref name="minAreaRatio"/> of the area it
        /// started with. <paramref name="scratch"/> is a working copy of the UVs, which this changes.
        /// </summary>
        internal static float LargestUVDragWithoutFolding(
            int[] triangles, int[] strokeTriangles, float[] startAreas, Vector2[] startUVs, Vector2[] scratch,
            int[] vertices, Vector2[] offsets, float minAreaRatio)
        {
            bool Holds(float share)
            {
                for (int k = 0; k < vertices.Length; k++)
                {
                    scratch[vertices[k]] = startUVs[vertices[k]] - (offsets[k] * share);
                }
                for (int t = 0; t < strokeTriangles.Length; t++)
                {
                    float before = startAreas[t];
                    if (Mathf.Abs(before) < 1e-14f)
                    {
                        continue;
                    }
                    int first = strokeTriangles[t];
                    float now = SignedUVArea(scratch[triangles[first]], scratch[triangles[first + 1]], scratch[triangles[first + 2]]);
                    if (now / before < minAreaRatio)
                    {
                        return false;
                    }
                }
                return true;
            }

            if (Holds(1f))
            {
                return 1f;
            }
            float low = 0f, high = 1f;
            for (int i = 0; i < 10; i++)
            {
                float middle = (low + high) * 0.5f;
                if (Holds(middle)) low = middle;
                else high = middle;
            }
            return low;
        }

        /// <summary>
        /// Turns the tangents of <paramref name="vertices"/> about their normals by as much as the change
        /// of UVs turned the direction of u on the surface there. <paramref name="triangleStarts"/> must
        /// hold every triangle that uses one of those vertices. Returns how many tangents changed.
        /// </summary>
        internal static int RotateTangentsForUVChange(
            int[] triangles, int[] triangleStarts, int[] vertices, Vector3[] positions, Vector3[] normals,
            Vector2[] uvBefore, Vector2[] uvAfter, Vector4[] tangents)
        {
            var before = new Vector3[positions.Length];
            var after = new Vector3[positions.Length];
            foreach (int first in triangleStarts)
            {
                int a = triangles[first], b = triangles[first + 1], c = triangles[first + 2];
                Vector3 was = TriangleTangent(positions[a], positions[b], positions[c], uvBefore[a], uvBefore[b], uvBefore[c]);
                Vector3 now = TriangleTangent(positions[a], positions[b], positions[c], uvAfter[a], uvAfter[b], uvAfter[c]);
                before[a] += was; before[b] += was; before[c] += was;
                after[a] += now; after[b] += now; after[c] += now;
            }
            int changed = 0;
            foreach (int vertex in vertices)
            {
                Vector3 normal = normals[vertex];
                if (normal.sqrMagnitude < 1e-12f)
                {
                    continue;
                }
                float turn = TangentTurn(before[vertex], after[vertex], normal);
                if (Mathf.Abs(turn) < TangentTurnThreshold)
                {
                    continue;
                }
                tangents[vertex] = TurnTangent(tangents[vertex], normal, turn);
                changed++;
            }
            return changed;
        }

        /// <summary>The angle, in degrees about <paramref name="normal"/>, from one tangent direction to another. 0 when either has no direction across the normal.</summary>
        internal static float TangentTurn(Vector3 from, Vector3 to, Vector3 normal)
        {
            Vector3 a = Vector3.ProjectOnPlane(from, normal);
            Vector3 b = Vector3.ProjectOnPlane(to, normal);
            if (a.sqrMagnitude < 1e-16f || b.sqrMagnitude < 1e-16f)
            {
                return 0f;
            }
            return Vector3.SignedAngle(a, b, normal);
        }

        /// <summary>A tangent turned about <paramref name="normal"/>, keeping its handedness (w).</summary>
        internal static Vector4 TurnTangent(Vector4 tangent, Vector3 normal, float degrees)
        {
            Vector3 turned = Quaternion.AngleAxis(degrees, normal) * new Vector3(tangent.x, tangent.y, tangent.z);
            return new Vector4(turned.x, turned.y, turned.z, tangent.w);
        }

        /// <summary>
        /// How the stage's UVs are scaled from the slot's own (the atlas mapping): baked = offset + scale * slot,
        /// per axis. False when the two do not match that way; <paramref name="worstError"/> is how far off
        /// the worst vertex is.
        /// </summary>
        internal static bool TryFitUVScale(Vector2[] slotUVs, Vector2[] bakedUVs, out Vector2 scale, out float worstError)
        {
            scale = Vector2.one;
            worstError = float.PositiveInfinity;
            if (slotUVs == null || bakedUVs == null || slotUVs.Length == 0 || slotUVs.Length != bakedUVs.Length)
            {
                return false;
            }
            int count = slotUVs.Length;
            double worst = 0.0;
            for (int axis = 0; axis < 2; axis++)
            {
                double meanSlot = 0.0, meanBaked = 0.0;
                for (int i = 0; i < count; i++)
                {
                    meanSlot += slotUVs[i][axis];
                    meanBaked += bakedUVs[i][axis];
                }
                meanSlot /= count;
                meanBaked /= count;
                double covariance = 0.0, variance = 0.0;
                for (int i = 0; i < count; i++)
                {
                    double d = slotUVs[i][axis] - meanSlot;
                    covariance += d * (bakedUVs[i][axis] - meanBaked);
                    variance += d * d;
                }
                // Every vertex at one value on this axis: nothing to measure the scale with.
                double axisScale = variance > 1e-12 ? covariance / variance : 1.0;
                if (axisScale < 1e-6)
                {
                    return false;
                }
                double offset = meanBaked - (axisScale * meanSlot);
                for (int i = 0; i < count; i++)
                {
                    worst = Math.Max(worst, Math.Abs(offset + (axisScale * slotUVs[i][axis]) - bakedUVs[i][axis]));
                }
                scale[axis] = (float)axisScale;
            }
            worstError = (float)worst;
            return true;
        }

        // ---- Slot state -----------------------------------------------------------

        /// <summary>Remembers a slot's UVs and tangents as they are before any tool changes them, and how its UVs are scaled into the stage's mesh.</summary>
        private void CaptureSculptUVState(SculptSlotEditState state)
        {
            if (state == null)
            {
                return;
            }
            state.originalUVs = null;
            state.originalTangents = null;
            state.uvEdited = false;
            state.uvScale = Vector2.one;
            state.uvScaleKnown = false;
            if (BakedMesh == null)
            {
                return;
            }
            Vector2[] uvs = BakedMesh.uv;
            if (uvs == null || state.start + state.count > uvs.Length)
            {
                return;
            }
            state.originalUVs = new Vector2[state.count];
            Array.Copy(uvs, state.start, state.originalUVs, 0, state.count);
            Vector4[] tangents = BakedMesh.tangents;
            if (tangents != null && state.start + state.count <= tangents.Length)
            {
                state.originalTangents = new Vector4[state.count];
                Array.Copy(tangents, state.start, state.originalTangents, 0, state.count);
            }
            Vector2[] slotUVs = state.slot != null && state.slot.asset != null && state.slot.asset.meshData != null
                ? state.slot.asset.meshData.uv
                : null;
            if (slotUVs == null || slotUVs.Length != state.count)
            {
                return;
            }
            if (TryFitUVScale(slotUVs, state.originalUVs, out Vector2 scale, out float worst) && worst <= 1e-4f)
            {
                state.uvScale = scale;
                state.uvScaleKnown = true;
            }
            else if (state.slot.UVArea.width > 1e-6f && state.slot.UVArea.height > 1e-6f)
            {
                // Some UVs were already changed on the way here (a Vertex UV modifier): the slot's
                // place in the atlas still gives the scale.
                state.uvScale = state.slot.UVArea.size;
                state.uvScaleKnown = true;
            }
        }

        /// <summary>The stage's UVs, read only when a UV tool has been used on some slot.</summary>
        private List<Vector2> GetSculptChangeUVs()
        {
            if (BakedMesh == null) return null;
            foreach (SculptSlotEditState state in sculptSlotStates.Values)
            {
                if (state != null && state.uvEdited)
                {
                    BakedMesh.GetUVs(0, sculptChangeUVs);
                    return sculptChangeUVs;
                }
            }
            return null;
        }

        private static bool SculptStateHasUVChanges(SculptSlotEditState state, List<Vector2> uvs)
        {
            if (state == null || !state.uvEdited || state.originalUVs == null || uvs == null ||
                state.start < 0 || state.start + state.count > uvs.Count)
            {
                return false;
            }
            for (int i = 0; i < state.count; i++)
            {
                if ((uvs[state.start + i] - state.originalUVs[i]).sqrMagnitude > UVChangedSqr) return true;
            }
            return false;
        }

        private bool SculptStateHasUVChanges(SculptSlotEditState state)
        {
            return state != null && state.uvEdited && SculptStateHasUVChanges(state, GetSculptChangeUVs());
        }

        /// <summary>True when a slot's vertices have moved (a blendshape can only hold that).</summary>
        private bool HasSculptPositionChanges()
        {
            if (BakedMesh == null) return false;
            BakedMesh.GetVertices(sculptChangeVertices);
            foreach (SculptSlotEditState state in sculptSlotStates.Values)
            {
                if (HasSculptChanges(state, sculptChangeVertices, null, null)) return true;
            }
            return false;
        }

        private SculptUVTopology GetUVTopology(SculptSlotEditState state, Vector3[] positions, Vector2[] uvs, int[] triangles)
        {
            if (!ReferenceEquals(uvTopologyMesh, BakedMesh))
            {
                uvTopologies.Clear();
                uvTopologyMesh = BakedMesh;
            }
            if (uvTopologies.TryGetValue(state, out SculptUVTopology topology) && topology.weld.Length == state.count)
            {
                return topology;
            }
            if (!brushTrianglesBySlot.TryGetValue(state.slot, out int[] slotTriangles))
            {
                return null;
            }
            float tolerance = Mathf.Max(0.000001f, BakedMesh.bounds.size.magnitude * 0.000001f);
            topology = BuildSculptUVTopology(state.start, state.count, slotTriangles, triangles, positions, uvs, tolerance, 0.00001f);
            uvTopologies[state] = topology;
            return topology;
        }

        // ---- The stroke -------------------------------------------------------------

        /// <summary>
        /// Starts a UV drag once Grab has worked out the active slot's brush weights. Returns false when
        /// there is nothing to drag, which ends the stroke.
        /// </summary>
        private bool BeginUVSculptDrag()
        {
            sculptStrokeIsUV = false;
            uvStrokeChanged = false;
            uvStrokeLimited = false;
            sculptUVStatusMessage = string.Empty;
            if (BakedMesh == null || activeSculptState == null || sculptGrabWeights == null)
            {
                return false;
            }
            if (IsEditingBlendshape)
            {
                sculptUVStatusMessage = "A blendshape is loaded, and a blendshape cannot hold UVs. Close the blendshape (Edit Blendshape) to change UVs.";
                return false;
            }
            int vertexCount = BakedMesh.vertexCount;
            Vector2[] uvs = BakedMesh.uv;
            if (uvs == null || uvs.Length != vertexCount)
            {
                sculptUVStatusMessage = "The character's mesh has no UVs.";
                return false;
            }
            if (!activeSculptState.uvScaleKnown)
            {
                sculptUVStatusMessage = "The UVs of '" + activeSculptState.slot.slotName + "' could not be matched to its slot asset, so they cannot be changed here.";
                return false;
            }
            Vector3[] positions = BakedMesh.vertices;
            int[] triangles = GetBakedTriangles();
            EnsureBrushTriangleCache(triangles);

            // The brush's weights where it was put down, as Grab works them out, and the most each
            // vertex can follow the drag (Effect and mask) if the middle of the brush reaches it.
            float strength = sculptStrengthPercent * 0.01f;
            var primary = new float[vertexCount];
            var primaryMax = new float[vertexCount];
            float[] mirrored = sculptGrabMirroredWeights != null ? new float[vertexCount] : null;
            float[] mirroredMax = mirrored != null ? new float[vertexCount] : null;
            uvStrokeStates.Clear();
            uvStrokeStates.Add(activeSculptState);
            for (int i = 0; i < sculptSlotVertexCount; i++)
            {
                int vertex = sculptSlotStart + i;
                float unmasked = strength * (sculptMask != null && i < sculptMask.Length ? 1f - sculptMask[i] : 1f);
                primary[vertex] = sculptGrabWeights[i];
                if (primary[vertex] > 0f) primaryMax[vertex] = unmasked;
                if (mirrored != null)
                {
                    mirrored[vertex] = sculptGrabMirroredWeights[i];
                    if (mirrored[vertex] > 0f) mirroredMax[vertex] = unmasked;
                }
            }
            if (IsSculptMultiSlotMode && !sculptConnectedOnly)
            {
                // The texture does not stop at the seam between two editable slots.
                AddUVWeightsOfOtherSlots(positions, primary, primaryMax, mirrored, mirroredMax);
            }

            // Copies of a vertex that share a UV move as one; island edges stay put when they are locked.
            foreach (SculptSlotEditState state in uvStrokeStates)
            {
                SculptUVTopology topology = GetUVTopology(state, positions, uvs, triangles);
                if (topology == null)
                {
                    for (int i = 0; i < state.count; i++)
                    {
                        primary[state.start + i] = 0f;
                        if (mirrored != null) mirrored[state.start + i] = 0f;
                    }
                    continue;
                }
                ShareUVWeights(state, topology, primary);
                ShareUVWeights(state, topology, primaryMax);
                if (mirrored != null)
                {
                    ShareUVWeights(state, topology, mirrored);
                    ShareUVWeights(state, topology, mirroredMax);
                }
            }

            // Each island's gradients of u and v, averaged over the brush.
            var gradU = new Vector3[vertexCount];
            var gradV = new Vector3[vertexCount];
            var areas = new float[vertexCount];
            var strokeTriangles = new List<int>();
            var strokeAreas = new List<float>();
            bool Moves(int vertex) => primary[vertex] > 0f || (mirrored != null && mirrored[vertex] > 0f);
            foreach (SculptSlotEditState state in uvStrokeStates)
            {
                if (!brushTrianglesBySlot.TryGetValue(state.slot, out int[] slotTriangles))
                {
                    continue;
                }
                foreach (int first in slotTriangles)
                {
                    int a = triangles[first], b = triangles[first + 1], c = triangles[first + 2];
                    if (!Moves(a) && !Moves(b) && !Moves(c))
                    {
                        continue;
                    }
                    strokeTriangles.Add(first);
                    strokeAreas.Add(SignedUVArea(uvs[a], uvs[b], uvs[c]));
                    if (TriangleUVGradients(positions[a], positions[b], positions[c], uvs[a], uvs[b], uvs[c],
                            out Vector3 triangleU, out Vector3 triangleV, out float area))
                    {
                        gradU[a] += triangleU * area; gradV[a] += triangleV * area; areas[a] += area;
                        gradU[b] += triangleU * area; gradV[b] += triangleV * area; areas[b] += area;
                        gradU[c] += triangleU * area; gradV[c] += triangleV * area; areas[c] += area;
                    }
                }
            }

            // The pointer's drag is on the view plane; each vertex's gradients are turned to take it
            // straight from there (its own part of the surface may face another way than the middle).
            Vector3[] normals = BakedMesh.normals;
            bool hasNormals = normals != null && normals.Length == vertexCount;
            Vector3 centre = sculptGrabStartLocalPoint;
            Vector3 mirroredCentre = ReflectLocalX(centre);
            Vector3 view = VertexObject != null ? VertexObject.transform.InverseTransformDirection(sculptGrabPlane.normal) : Vector3.zero;
            Vector3 mirroredView = ReflectLocalX(view);
            float inverseRadius = 1f / Mathf.Max(sculptRadius, 1e-6f);

            var affected = new List<int>();
            var affectedIsland = new List<int>();
            var islandIndex = new Dictionary<long, int>();
            var islandU = new List<Vector3>();
            var islandV = new List<Vector3>();
            var islandWeight = new List<float>();
            var islandMirroredU = new List<Vector3>();
            var islandMirroredV = new List<Vector3>();
            var islandMirroredWeight = new List<float>();
            for (int s = 0; s < uvStrokeStates.Count; s++)
            {
                SculptSlotEditState state = uvStrokeStates[s];
                if (!uvTopologies.TryGetValue(state, out SculptUVTopology topology))
                {
                    continue;
                }
                for (int i = 0; i < state.count; i++)
                {
                    int vertex = state.start + i;
                    if (!Moves(vertex) || areas[vertex] <= 0f)
                    {
                        continue;
                    }
                    long key = ((long)s << 32) | (uint)topology.island[i];
                    if (!islandIndex.TryGetValue(key, out int island))
                    {
                        island = islandU.Count;
                        islandIndex.Add(key, island);
                        islandU.Add(Vector3.zero); islandV.Add(Vector3.zero); islandWeight.Add(0f);
                        islandMirroredU.Add(Vector3.zero); islandMirroredV.Add(Vector3.zero); islandMirroredWeight.Add(0f);
                    }
                    Vector3 u = gradU[vertex] / areas[vertex];
                    Vector3 v = gradV[vertex] / areas[vertex];
                    Vector3 normal = hasNormals ? normals[vertex] : Vector3.zero;
                    if (primary[vertex] > 0f)
                    {
                        float weight = GradientAveragingWeight((positions[vertex] - centre).magnitude * inverseRadius);
                        islandU[island] += ViewCorrectedGradient(u, normal, view) * weight;
                        islandV[island] += ViewCorrectedGradient(v, normal, view) * weight;
                        islandWeight[island] += weight;
                    }
                    if (mirrored != null && mirrored[vertex] > 0f)
                    {
                        float mirroredWeight = GradientAveragingWeight((positions[vertex] - mirroredCentre).magnitude * inverseRadius);
                        islandMirroredU[island] += ViewCorrectedGradient(u, normal, mirroredView) * mirroredWeight;
                        islandMirroredV[island] += ViewCorrectedGradient(v, normal, mirroredView) * mirroredWeight;
                        islandMirroredWeight[island] += mirroredWeight;
                    }
                    affected.Add(vertex);
                    affectedIsland.Add(island);
                }
            }
            if (affected.Count == 0)
            {
                uvStrokeStates.Clear();
                return false;
            }

            int count = affected.Count;
            uvStrokeVertices = affected.ToArray();
            uvStrokeMaxWeights = new float[count];
            uvStrokeGradU = new Vector3[count];
            uvStrokeGradV = new Vector3[count];
            uvStrokeMirroredMaxWeights = mirrored != null ? new float[count] : null;
            uvStrokeMirroredGradU = mirrored != null ? new Vector3[count] : null;
            uvStrokeMirroredGradV = mirrored != null ? new Vector3[count] : null;
            uvStrokeOffsets = new Vector2[count];
            for (int k = 0; k < count; k++)
            {
                int vertex = uvStrokeVertices[k];
                int island = affectedIsland[k];
                uvStrokeMaxWeights[k] = primaryMax[vertex];
                if (islandWeight[island] > 1e-20f)
                {
                    uvStrokeGradU[k] = islandU[island] / islandWeight[island];
                    uvStrokeGradV[k] = islandV[island] / islandWeight[island];
                }
                if (mirrored != null)
                {
                    uvStrokeMirroredMaxWeights[k] = mirroredMax[vertex];
                    if (islandMirroredWeight[island] > 1e-20f)
                    {
                        uvStrokeMirroredGradU[k] = islandMirroredU[island] / islandMirroredWeight[island];
                        uvStrokeMirroredGradV[k] = islandMirroredV[island] / islandMirroredWeight[island];
                    }
                }
            }
            uvStrokeTriangles = strokeTriangles.ToArray();
            uvStrokeTriangleAreas = strokeAreas.ToArray();
            // The island under the pointer, and the one under its mirror image.
            var islandOfVertex = new Dictionary<int, int>(count);
            for (int k = 0; k < count; k++) islandOfVertex[uvStrokeVertices[k]] = affectedIsland[k];
            FindUVCentreIsland(centre, positions, uvs, triangles, islandOfVertex, affectedIsland,
                out uvStrokeCentreTriangles, out uvStrokeInCentreIsland, out uvStrokeCentreUV);
            uvStrokeMirroredCentreTriangles = null;
            uvStrokeInMirroredCentreIsland = null;
            if (mirrored != null)
            {
                FindUVCentreIsland(mirroredCentre, positions, uvs, triangles, islandOfVertex, affectedIsland,
                    out uvStrokeMirroredCentreTriangles, out uvStrokeInMirroredCentreIsland, out uvStrokeMirroredCentreUV);
            }
            uvStrokeStart = uvs;
            uvStrokeUVs = (Vector2[])uvs.Clone();
            uvStrokeScratch = (Vector2[])uvs.Clone();
            uvStrokePositions = positions;
            uvStrokeCentre = centre;
            uvStrokeMirroredCentre = mirroredCentre;
            uvStrokeNormal = sculptGrabStartLocalNormal;
            uvStrokeViewDirection = view;
            sculptStrokeIsUV = true;
            return true;
        }

        /// <summary>
        /// The UV island under a point of the brush: its triangles among the stroke's, which of the
        /// stroke's vertices are in it, and the UV at the point. No triangles when nothing is there.
        /// </summary>
        private void FindUVCentreIsland(
            Vector3 point, Vector3[] positions, Vector2[] uvs, int[] triangles, Dictionary<int, int> islandOfVertex, List<int> affectedIsland,
            out int[] islandTriangles, out bool[] inIsland, out Vector2 uvAtPoint)
        {
            islandTriangles = null;
            inIsland = null;
            uvAtPoint = Vector2.zero;
            if (!ClosestPointOnFan(point, uvStrokeTriangles, triangles, positions, out Vector3 closest, out int triangle, out Vector3 weights) ||
                (closest - point).sqrMagnitude > sculptRadius * sculptRadius)
            {
                return;
            }
            int island = IslandOfTriangle(triangle, triangles, islandOfVertex);
            if (island < 0)
            {
                return;
            }
            uvAtPoint = (uvs[triangles[triangle]] * weights.x) + (uvs[triangles[triangle + 1]] * weights.y) + (uvs[triangles[triangle + 2]] * weights.z);
            var list = new List<int>();
            foreach (int first in uvStrokeTriangles)
            {
                if (IslandOfTriangle(first, triangles, islandOfVertex) == island) list.Add(first);
            }
            islandTriangles = list.ToArray();
            inIsland = new bool[affectedIsland.Count];
            for (int k = 0; k < inIsland.Length; k++) inIsland[k] = affectedIsland[k] == island;
        }

        /// <summary>A triangle is in one island; the stroke knows the island of each of its vertices that moves.</summary>
        private static int IslandOfTriangle(int first, int[] triangles, Dictionary<int, int> islandOfVertex)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                if (islandOfVertex.TryGetValue(triangles[first + corner], out int island)) return island;
            }
            return -1;
        }

        /// <summary>
        /// How far the UV is, on the island under the pointer, from where the brush was put down to
        /// where the texture has been dragged: that island's whole movement in the texture.
        /// </summary>
        private bool TryReadUVDrag(int[] islandTriangles, Vector3 centre, Vector2 centreUV, Vector3 surfaceDrag, out Vector2 uvDrag)
        {
            uvDrag = Vector2.zero;
            if (islandTriangles == null || islandTriangles.Length == 0)
            {
                return false;
            }
            int[] triangles = GetBakedTriangles();
            if (!ClosestPointOnFan(centre + surfaceDrag, islandTriangles, triangles, uvStrokePositions, out _, out int triangle, out Vector3 weights))
            {
                return false;
            }
            uvDrag = (uvStrokeStart[triangles[triangle]] * weights.x) + (uvStrokeStart[triangles[triangle + 1]] * weights.y) +
                     (uvStrokeStart[triangles[triangle + 2]] * weights.z) - centreUV;
            return true;
        }

        /// <summary>Brush weights on the editable slots other than the one under the pointer, as Grab works them out for that one.</summary>
        private void AddUVWeightsOfOtherSlots(Vector3[] positions, float[] primary, float[] primaryMax, float[] mirrored, float[] mirroredMax)
        {
            float strength = sculptStrengthPercent * 0.01f;
            Vector3 point = sculptGrabStartLocalPoint;
            Vector3 normal = sculptGrabStartLocalNormal;
            Vector3 mirroredPoint = ReflectLocalX(point);
            Vector3 mirroredNormal = ReflectLocalX(normal).normalized;
            float radiusSquared = sculptRadius * sculptRadius;
            foreach (SlotData slot in sculptSlots)
            {
                if (slot == null || ReferenceEquals(slot, sculptSlot))
                {
                    continue;
                }
                SculptSlotEditState state = GetOrCreateSculptSlotState(slot);
                if (state == null || !state.uvScaleKnown || state.start < 0 || state.start + state.count > positions.Length)
                {
                    continue;
                }
                float[] mask = GetSculptStateMask(state);
                bool any = false;
                for (int i = 0; i < state.count; i++)
                {
                    int vertex = state.start + i;
                    float unmasked = strength * (mask != null && i < mask.Length ? 1f - mask[i] : 1f);
                    float distanceSquared = (positions[vertex] - point).sqrMagnitude;
                    if (distanceSquared <= radiusSquared && SculptVertexFacesBrush(vertex, normal))
                    {
                        primary[vertex] = EvaluateSculptFalloff(Mathf.Sqrt(distanceSquared) / sculptRadius) * unmasked;
                        if (primary[vertex] > 0f) primaryMax[vertex] = unmasked;
                        any |= primary[vertex] > 0f;
                    }
                    if (mirrored != null)
                    {
                        float mirroredDistanceSquared = (positions[vertex] - mirroredPoint).sqrMagnitude;
                        if (mirroredDistanceSquared <= radiusSquared && SculptVertexFacesBrush(vertex, mirroredNormal))
                        {
                            mirrored[vertex] = EvaluateSculptFalloff(Mathf.Sqrt(mirroredDistanceSquared) / sculptRadius) * unmasked;
                            if (mirrored[vertex] > 0f) mirroredMax[vertex] = unmasked;
                            any |= mirrored[vertex] > 0f;
                        }
                    }
                }
                if (any)
                {
                    uvStrokeStates.Add(state);
                }
            }
        }

        private void ShareUVWeights(SculptSlotEditState state, SculptUVTopology topology, float[] weights)
        {
            for (int i = 0; i < state.count; i++)
            {
                int first = state.start + topology.weld[i];
                if (weights[state.start + i] > weights[first]) weights[first] = weights[state.start + i];
            }
            for (int i = 0; i < state.count; i++)
            {
                weights[state.start + i] = sculptUVLockBorders && topology.border[i] ? 0f : weights[state.start + topology.weld[i]];
            }
        }

        /// <summary>
        /// The drag so far, measured on the view plane in the mesh's space: moves the texture under the
        /// brush so that it stays under the pointer.
        /// </summary>
        private void ApplyUVSculptDrag(Vector3 viewPlaneDelta)
        {
            if (!sculptStrokeIsUV || uvStrokeVertices == null || BakedMesh == null ||
                uvStrokeUVs == null || uvStrokeUVs.Length != BakedMesh.vertexCount)
            {
                return;
            }
            // Where the texture travels across the surface (which sets how far each vertex follows),
            // and the drag as the pointer made it (which the gradients were turned to take).
            Vector3 localDelta = DragOnSurface(viewPlaneDelta, uvStrokeNormal, uvStrokeViewDirection);
            Vector3 mirroredDelta = ReflectLocalX(localDelta);
            Vector3 mirroredViewPlaneDelta = ReflectLocalX(viewPlaneDelta);
            Func<float, float> falloff = EvaluateSculptFalloff;
            // The island under the pointer moves by what the mesh says the UV is where the pointer now is.
            bool readCentre = TryReadUVDrag(uvStrokeCentreTriangles, uvStrokeCentre, uvStrokeCentreUV, localDelta, out Vector2 centreDrag);
            Vector2 mirroredCentreDrag = Vector2.zero;
            bool readMirroredCentre = uvStrokeMirroredMaxWeights != null && TryReadUVDrag(
                uvStrokeMirroredCentreTriangles, uvStrokeMirroredCentre, uvStrokeMirroredCentreUV, mirroredDelta, out mirroredCentreDrag);
            float furthest = 0f;
            for (int k = 0; k < uvStrokeVertices.Length; k++)
            {
                Vector3 position = uvStrokePositions[uvStrokeVertices[k]];
                float weight = DraggedWeight(uvStrokeMaxWeights[k], position, uvStrokeCentre, localDelta, sculptRadius, falloff);
                furthest = Mathf.Max(furthest, uvStrokeMaxWeights[k]);
                Vector2 offset = readCentre && uvStrokeInCentreIsland[k]
                    ? centreDrag * weight
                    : new Vector2(Vector3.Dot(uvStrokeGradU[k], viewPlaneDelta), Vector3.Dot(uvStrokeGradV[k], viewPlaneDelta)) * weight;
                if (uvStrokeMirroredMaxWeights != null)
                {
                    float mirroredWeight = DraggedWeight(
                        uvStrokeMirroredMaxWeights[k], position, uvStrokeMirroredCentre, mirroredDelta, sculptRadius, falloff);
                    offset += readMirroredCentre && uvStrokeInMirroredCentreIsland[k]
                        ? mirroredCentreDrag * mirroredWeight
                        : new Vector2(
                              Vector3.Dot(uvStrokeMirroredGradU[k], mirroredViewPlaneDelta),
                              Vector3.Dot(uvStrokeMirroredGradV[k], mirroredViewPlaneDelta)) * mirroredWeight;
                }
                uvStrokeOffsets[k] = offset;
            }
            float share = LargestUVDragWithoutFolding(
                GetBakedTriangles(), uvStrokeTriangles, uvStrokeTriangleAreas, uvStrokeStart, uvStrokeScratch,
                uvStrokeVertices, uvStrokeOffsets, UVMinAreaRatio);
            // To show a point of the texture further along, a vertex takes the UV from further back.
            for (int k = 0; k < uvStrokeVertices.Length; k++)
            {
                int vertex = uvStrokeVertices[k];
                uvStrokeUVs[vertex] = uvStrokeStart[vertex] - (uvStrokeOffsets[k] * share);
            }
            BakedMesh.uv = uvStrokeUVs;
            uvStrokeChanged = true;
            // The texture cannot leave the brush: past about two thirds of the radius it bunches up at the edge.
            bool limited = share < 1f || localDelta.magnitude * furthest > sculptRadius * 0.66f;
            if (uvStrokeLimited != limited)
            {
                uvStrokeLimited = limited;
                sculptUVStatusMessage = limited
                    ? "The drag is long for this brush: the texture bunches up at the brush's edge and stops before it would fold over. Use a larger radius to move it further."
                    : string.Empty;
                RepaintLinkedEditors();
            }
            EditorUtility.SetDirty(BakedMesh);
            EditorUtility.SetDirty(this);
        }

        /// <summary>Ends a UV stroke. Returns false when the stroke was not one.</summary>
        private bool EndUVSculptStroke(bool finalize)
        {
            if (!sculptStrokeIsUV)
            {
                return false;
            }
            sculptStrokeIsUV = false;
            if (uvStrokeChanged && BakedMesh != null && uvStrokeUVs != null && uvStrokeUVs.Length == BakedMesh.vertexCount)
            {
                // The direction of u on the surface turned with the texture; normal maps are lit by it.
                Vector4[] tangents = BakedMesh.tangents;
                Vector3[] normals = BakedMesh.normals;
                if (tangents != null && tangents.Length == BakedMesh.vertexCount && normals != null && normals.Length == tangents.Length &&
                    RotateTangentsForUVChange(GetBakedTriangles(), uvStrokeTriangles, uvStrokeVertices, uvStrokePositions, normals,
                        uvStrokeStart, uvStrokeUVs, tangents) > 0)
                {
                    BakedMesh.tangents = tangents;
                }
                foreach (SculptSlotEditState state in uvStrokeStates)
                {
                    state.uvEdited = true;
                }
                unsavedWorkCache = null;
                sculptChangesUiDirty = true;
                EditorUtility.SetDirty(BakedMesh);
                EditorUtility.SetDirty(this);
            }
            uvStrokeChanged = false;
            uvStrokeStart = null;
            uvStrokeUVs = null;
            uvStrokeScratch = null;
            uvStrokePositions = null;
            uvStrokeVertices = null;
            uvStrokeMaxWeights = null;
            uvStrokeMirroredMaxWeights = null;
            uvStrokeGradU = null;
            uvStrokeGradV = null;
            uvStrokeMirroredGradU = null;
            uvStrokeMirroredGradV = null;
            uvStrokeOffsets = null;
            uvStrokeCentreTriangles = null;
            uvStrokeInCentreIsland = null;
            uvStrokeMirroredCentreTriangles = null;
            uvStrokeInMirroredCentreIsland = null;
            uvStrokeTriangles = null;
            uvStrokeTriangleAreas = null;
            uvStrokeStates.Clear();
            if (finalize)
            {
                RepaintLinkedEditors();
                SceneView.RepaintAll();
            }
            return true;
        }

        // ---- Whole-slot operations -------------------------------------------------

        /// <summary>Puts the editable slots' UVs back to what they were when the editor opened (or were last saved into the slot). One Undo step. Returns how many slots changed.</summary>
        public int ResetEditableSlotUVs()
        {
            EndSculptStroke(true);
            if (BakedMesh == null)
            {
                return 0;
            }
            Vector2[] uvs = BakedMesh.uv;
            Vector4[] tangents = BakedMesh.tangents;
            Vector3[] normals = BakedMesh.normals;
            bool turnTangents = tangents != null && tangents.Length == BakedMesh.vertexCount && normals != null && normals.Length == tangents.Length;
            int changedSlots = 0;
            foreach (SlotData slot in GetEditableSlots())
            {
                if (!sculptSlotStates.TryGetValue(slot, out SculptSlotEditState state) || state == null || !state.uvEdited ||
                    state.originalUVs == null || state.start < 0 || state.start + state.count > uvs.Length)
                {
                    continue;
                }
                if (changedSlots == 0)
                {
                    Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, "Reset UVs");
                }
                Array.Copy(state.originalUVs, 0, uvs, state.start, state.count);
                if (turnTangents && state.originalTangents != null)
                {
                    for (int i = 0; i < state.count; i++)
                    {
                        int vertex = state.start + i;
                        float turn = TangentTurn(state.originalTangents[i], tangents[vertex], normals[vertex]);
                        if (Mathf.Abs(turn) >= TangentTurnThreshold)
                        {
                            tangents[vertex] = TurnTangent(tangents[vertex], normals[vertex], -turn);
                        }
                    }
                }
                changedSlots++;
            }
            if (changedSlots > 0)
            {
                BakedMesh.uv = uvs;
                if (turnTangents) BakedMesh.tangents = tangents;
                unsavedWorkCache = null;
                sculptChangesUiDirty = true;
                EditorUtility.SetDirty(BakedMesh);
                EditorUtility.SetDirty(this);
            }
            sculptUVStatusMessage = changedSlots == 0
                ? "No editable slot has changed UVs."
                : "Reset the UVs of " + changedSlots + (changedSlots == 1 ? " slot." : " slots.");
            RepaintLinkedEditors();
            SceneView.RepaintAll();
            return changedSlots;
        }

        // ---- Saving -----------------------------------------------------------------

        /// <summary>A stage UV change, taken out of the atlas mapping into the slot's own UV space.</summary>
        private static Vector2 BakedToSlotUVDelta(SculptSlotEditState state, Vector2 bakedDelta)
        {
            return new Vector2(bakedDelta.x / state.uvScale.x, bakedDelta.y / state.uvScale.y);
        }

        /// <summary>
        /// Puts a slot's changed UVs, and the tangents turned with them, on a copy of its mesh data.
        /// Returns how many UVs changed.
        /// </summary>
        private int ApplySculptedUVs(SculptSlotEditState state, UMAMeshData meshData)
        {
            if (state == null || !state.uvEdited || !state.uvScaleKnown || state.originalUVs == null || meshData == null ||
                meshData.uv == null || meshData.uv.Length != state.count || BakedMesh == null)
            {
                return 0;
            }
            Vector2[] uvs = BakedMesh.uv;
            if (uvs == null || state.start < 0 || state.start + state.count > uvs.Length)
            {
                return 0;
            }
            Vector4[] tangents = BakedMesh.tangents;
            Vector3[] normals = BakedMesh.normals;
            bool turnTangents = state.originalTangents != null && tangents != null && normals != null &&
                                state.start + state.count <= tangents.Length && tangents.Length == normals.Length &&
                                meshData.tangents != null && meshData.tangents.Length == state.count &&
                                meshData.normals != null && meshData.normals.Length == state.count;
            int changed = 0;
            for (int i = 0; i < state.count; i++)
            {
                int vertex = state.start + i;
                Vector2 delta = uvs[vertex] - state.originalUVs[i];
                if (delta.sqrMagnitude > UVChangedSqr)
                {
                    meshData.uv[i] += BakedToSlotUVDelta(state, delta);
                    changed++;
                }
                if (turnTangents)
                {
                    float turn = TangentTurn(state.originalTangents[i], tangents[vertex], normals[vertex]);
                    if (Mathf.Abs(turn) >= TangentTurnThreshold)
                    {
                        meshData.tangents[i] = TurnTangent(meshData.tangents[i], meshData.normals[i], turn);
                    }
                }
            }
            return changed;
        }

        /// <summary>
        /// A Vertex UV modifier for a slot whose UVs a UV tool changed: the new UV of each changed vertex,
        /// in the slot's own UV space. Null when nothing changed.
        /// </summary>
        private MeshModifier.Modifier CreateSculptUVModifier(SculptSlotEditState state, bool useSlotSuffix)
        {
            if (state == null || !state.uvEdited || !state.uvScaleKnown || state.originalUVs == null ||
                state.slot == null || state.slot.asset == null || BakedMesh == null)
            {
                return null;
            }
            UMAMeshData meshData = state.slot.asset.meshData;
            Vector2[] uvs = BakedMesh.uv;
            if (UMAMeshData.IsNullOrEmptyMeshData(meshData) || meshData.uv == null || meshData.uv.Length != state.count ||
                uvs == null || state.start < 0 || state.start + state.count > uvs.Length)
            {
                return null;
            }
            string slotKey = GetSculptSlotKey(state.slot);
            string baseName = string.IsNullOrWhiteSpace(sculptModifierName) ? "Sculpt" : sculptModifierName;
            var modifier = new MeshModifier.Modifier
            {
                ModifierName = (useSlotSuffix ? baseName + " - " + state.slot.slotName : baseName) + " UVs",
                SlotName = slotKey,
                Scale = 1f,
                DNAName = string.Empty,
                manuallyModified = true,
                adjustments = new VertexUVAdjustmentCollection()
            };
            for (int i = 0; i < state.count; i++)
            {
                Vector2 delta = uvs[state.start + i] - state.originalUVs[i];
                if (delta.sqrMagnitude <= UVChangedSqr)
                {
                    continue;
                }
                modifier.adjustments.vertexAdjustments.Add(new VertexUVAdjustment
                {
                    vertexIndex = i,
                    slotName = slotKey,
                    weight = 1f,
                    uv = meshData.uv[i] + BakedToSlotUVDelta(state, delta)
                });
            }
            return modifier.adjustments.vertexAdjustments.Count > 0 ? modifier : null;
        }

        // ---- Panel --------------------------------------------------------------------

        /// <summary>UV Sculpt needs the character's own materials: the pastel editor materials have no texture to see.</summary>
        private void ShowMaterialsForUVSculpt()
        {
            if (showOriginalMaterials)
            {
                return;
            }
            showOriginalMaterials = true;
            showVertexColorsView = false;
            ApplyVertexDisplayOptions();
            SceneView.RepaintAll();
        }

        private void DrawUVSculptOptions()
        {
            EditorGUILayout.HelpBox(
                "Press on the surface and drag: the texture under the brush follows the pointer and fades out toward the edge of the brush by the falloff. " +
                "Vertices do not move. Effect % sets how closely the texture follows the pointer (100 keeps it under the pointer). " +
                "The texture stops where it would fold over itself; use a larger radius to move it further.",
                MessageType.None);
            if (IsEditingBlendshape)
            {
                EditorGUILayout.HelpBox("A blendshape is loaded, and a blendshape cannot hold UVs. Close it to change UVs.", MessageType.Warning);
            }
            if (!showOriginalMaterials)
            {
                EditorGUILayout.HelpBox("The editor materials have no texture. Turn on Original Materials to see the texture move.", MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Show Original Materials", "Display the character's own materials, with their textures.")))
                {
                    ShowMaterialsForUVSculpt();
                }
            }
            sculptUVLockBorders = EditorGUILayout.Toggle(
                new GUIContent(
                    "Lock Borders",
                    "Leave the UVs on the edges of UV islands (the UV seams) and on open edges of the mesh as they are. " +
                    "The islands then keep their outline in the texture, and the texture next to a seam is squeezed or stretched instead of moving across it."),
                sculptUVLockBorders);
            if (GUILayout.Button(new GUIContent("Reset UVs", "Put the editable slots' UVs back to what they were when the editor opened, or were last saved into the slot. One Undo step.")))
            {
                ResetEditableSlotUVs();
                GUIUtility.ExitGUI();
            }
            if (!string.IsNullOrEmpty(sculptUVStatusMessage))
            {
                EditorGUILayout.LabelField(sculptUVStatusMessage, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
