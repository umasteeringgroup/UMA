using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Beautify: straightens the lines of the mesh. A vertex under the brush slides along the surface
    /// until it is in line with its neighbours on either side, along each edge loop through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slots are stored as triangles, so the modelled quads are found first: an edge that is the
    /// longest edge of both triangles it joins is the diagonal of a quad, and is not a line of the
    /// mesh. What is left around a regular vertex are four edges, two each way; the vertex belongs on
    /// the line between each opposite pair, so its place is where those two lines meet. A vertex with
    /// six edges (a triangle mesh) has three such lines and goes to the point nearest all of them.
    /// Any other vertex (a pole) goes to the middle of its neighbours. Even Spacing also draws a
    /// vertex toward the middle of each line; at 0 the spacing along the lines is kept as modelled,
    /// at 1 the vertex goes to the middle of its neighbours on those lines.
    /// </para>
    /// <para>
    /// Smooth, by comparison, moves every vertex to the middle of all its neighbours, diagonals
    /// included, which shrinks the surface and evens out spacing that was modelled on purpose.
    /// With Keep Shape on, Beautify moves a vertex only across the surface and puts it back on the
    /// triangles it left. With Keep Texture on, the vertex takes the UV of the place it moved to, so
    /// the texture stays where it is on the body; a vertex on a UV seam has no single UV to take and
    /// stays where it is. Open edges, which include the edges a slot shares with its neighbours,
    /// never move.
    /// </para>
    /// </remarks>
    public partial class VertexEditorStage
    {
        /// <summary>An edge counts as a quad's diagonal when it is this much longer than the other edges of both its triangles.</summary>
        private const float BeautifyDiagonalMargin = 1.02f;

        // Remembered with the other tool choices (VertexEditorToolSettings).
        private float sculptBeautifyEvenSpacing;
        private bool sculptBeautifyKeepShape = true;
        private bool sculptBeautifyKeepTexture = true;

        private BeautifyTopology beautifyTopology;
        private SculptSlotEditState beautifyTopologyState;
        private Mesh beautifyTopologyMesh;
        private readonly Vector3[] beautifyRing = new Vector3[BeautifyMaxRing];
        private const int BeautifyMaxRing = 16;

        // The stage's UVs while a Beautify stroke with Keep Texture is applied; null otherwise.
        private Vector2[] beautifyUVs;
        private Vector2[] beautifyUVBefore;
        private bool beautifyUVChanged;

        /// <summary>The lines of a slot's mesh, on vertices welded by position.</summary>
        internal sealed class BeautifyTopology
        {
            /// <summary>Per vertex: the first vertex at its position.</summary>
            public int[] group;
            /// <summary>Per first vertex: the first vertices it shares a modelled edge with (no quad diagonals), in order around it.</summary>
            public int[][] ring;
            /// <summary>Per first vertex: the first indices of the triangles around it.</summary>
            public int[][] fan;
            /// <summary>Per first vertex: on an open edge of the slot, which never moves.</summary>
            public bool[] open;
            /// <summary>Per first vertex: its copies have different UVs (a UV seam).</summary>
            public bool[] seam;
        }

        // ---- Geometry -----------------------------------------------------------

        /// <summary>
        /// Finds the lines of a slot's mesh. The slot's vertices are [start, start + count) of the arrays;
        /// <paramref name="groupOf"/> gives each one's first vertex at the same position (null: no vertex
        /// shares a position); <paramref name="slotTriangles"/> are the first indices of the slot's
        /// triangles in <paramref name="triangles"/>.
        /// </summary>
        internal static BeautifyTopology BuildBeautifyTopology(
            int start, int count, int[] slotTriangles, int[] triangles, Vector3[] positions, Vector2[] uvs, int[] groupOf)
        {
            var topology = new BeautifyTopology
            {
                group = new int[count],
                ring = new int[count][],
                fan = new int[count][],
                open = new bool[count],
                seam = new bool[count],
            };
            for (int i = 0; i < count; i++)
            {
                topology.group[i] = groupOf != null && groupOf[i] >= 0 && groupOf[i] < count ? groupOf[i] : i;
            }
            if (uvs != null)
            {
                for (int i = 0; i < count; i++)
                {
                    int first = topology.group[i];
                    if (first != i && (uvs[start + i] - uvs[start + first]).sqrMagnitude > 1e-10f)
                    {
                        topology.seam[first] = true;
                    }
                }
            }

            // Edges between welded vertices: how many triangles use each, and for how many it is the longest edge.
            var edgeUses = new Dictionary<long, int>();
            var edgeLongest = new Dictionary<long, int>();
            var fans = new List<int>[count];
            var normals = new Vector3[count];
            var seen = new HashSet<(int, int, int)>();
            long Key(int x, int y) => x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
            void Use(long key)
            {
                edgeUses.TryGetValue(key, out int uses);
                edgeUses[key] = uses + 1;
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
                    a = topology.group[a];
                    b = topology.group[b];
                    c = topology.group[c];
                    if (a == b || b == c || c == a)
                    {
                        continue;
                    }
                    // The reversed copy of a double-sided triangle is the same piece of surface.
                    int low = Mathf.Min(a, Mathf.Min(b, c)), high = Mathf.Max(a, Mathf.Max(b, c));
                    if (!seen.Add((low, a + b + c - low - high, high)))
                    {
                        continue;
                    }
                    Vector3 pa = positions[start + a], pb = positions[start + b], pc = positions[start + c];
                    long ab = Key(a, b), bc = Key(b, c), ca = Key(c, a);
                    Use(ab); Use(bc); Use(ca);
                    float lab = (pb - pa).magnitude, lbc = (pc - pb).magnitude, lca = (pa - pc).magnitude;
                    long longest = -1;
                    if (lab >= lbc * BeautifyDiagonalMargin && lab >= lca * BeautifyDiagonalMargin) longest = ab;
                    else if (lbc >= lab * BeautifyDiagonalMargin && lbc >= lca * BeautifyDiagonalMargin) longest = bc;
                    else if (lca >= lab * BeautifyDiagonalMargin && lca >= lbc * BeautifyDiagonalMargin) longest = ca;
                    if (longest >= 0)
                    {
                        edgeLongest.TryGetValue(longest, out int votes);
                        edgeLongest[longest] = votes + 1;
                    }
                    Vector3 normal = Vector3.Cross(pb - pa, pc - pa);
                    AddToFan(a); AddToFan(b); AddToFan(c);

                    void AddToFan(int vertex)
                    {
                        (fans[vertex] ?? (fans[vertex] = new List<int>(8))).Add(first);
                        // Both sides of double-sided geometry count the same way.
                        normals[vertex] += Vector3.Dot(normal, normals[vertex]) < 0f ? -normal : normal;
                    }
                }
            }

            var rings = new List<int>[count];
            foreach (KeyValuePair<long, int> edge in edgeUses)
            {
                int x = (int)(edge.Key >> 32), y = (int)(edge.Key & 0xFFFFFFFFL);
                if (edge.Value != 2)
                {
                    // An open edge, or one where more than two faces meet: its ends stay where they are.
                    topology.open[x] = true;
                    topology.open[y] = true;
                }
                else if (edgeLongest.TryGetValue(edge.Key, out int votes) && votes == 2)
                {
                    continue; // the diagonal of a quad
                }
                (rings[x] ?? (rings[x] = new List<int>(6))).Add(y);
                (rings[y] ?? (rings[y] = new List<int>(6))).Add(x);
            }

            var angles = new List<KeyValuePair<float, int>>(8);
            for (int i = 0; i < count; i++)
            {
                if (fans[i] != null) topology.fan[i] = fans[i].ToArray();
                if (rings[i] == null)
                {
                    continue;
                }
                // In order around the vertex, so that opposite neighbours are half the ring apart.
                Vector3 normal = normals[i].sqrMagnitude > 1e-24f ? normals[i].normalized : Vector3.up;
                Vector3 axisX = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 axisY = Vector3.Cross(normal, axisX);
                Vector3 centre = positions[start + i];
                angles.Clear();
                foreach (int neighbour in rings[i])
                {
                    Vector3 direction = positions[start + neighbour] - centre;
                    angles.Add(new KeyValuePair<float, int>(Mathf.Atan2(Vector3.Dot(direction, axisY), Vector3.Dot(direction, axisX)), neighbour));
                }
                angles.Sort((left, right) => left.Key.CompareTo(right.Key));
                var ring = new int[angles.Count];
                for (int k = 0; k < ring.Length; k++) ring[k] = angles[k].Value;
                topology.ring[i] = ring;
            }
            return topology;
        }

        /// <summary>
        /// Where a vertex belongs among its neighbours. <paramref name="ring"/> holds the first
        /// <paramref name="ringCount"/> neighbours in order around the vertex. Four or six neighbours:
        /// the point nearest the lines through each opposite pair, also drawn toward the middle of each
        /// line by <paramref name="evenSpacing"/> (0 to 1; at 1 it is the middle of the neighbours).
        /// Otherwise: the middle of the neighbours.
        /// </summary>
        internal static Vector3 BeautifyTarget(Vector3 position, Vector3[] ring, int ringCount, float evenSpacing)
        {
            if (ringCount < 2)
            {
                return position;
            }
            if (ringCount != 4 && ringCount != 6)
            {
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < ringCount; i++) sum += ring[i];
                return sum / ringCount;
            }

            // Least squares over the lines, measured from the vertex so the numbers stay small.
            // A little weight on staying put keeps this solvable when the lines are nearly parallel.
            const double stay = 1e-4;
            double spacing = Mathf.Clamp01(evenSpacing);
            double m00 = stay, m01 = 0, m02 = 0, m11 = stay, m12 = 0, m22 = stay;
            double r0 = 0, r1 = 0, r2 = 0;
            int half = ringCount / 2;
            for (int i = 0; i < half; i++)
            {
                Vector3 a = ring[i] - position;
                Vector3 b = ring[i + half] - position;
                Vector3 along = b - a;
                float length = along.magnitude;
                if (length < 1e-9f)
                {
                    continue;
                }
                double dx = along.x / length, dy = along.y / length, dz = along.z / length;
                // Across the line the vertex is pulled onto it; along it, toward the middle by the spacing.
                double across = 1.0;
                double weightAlong = spacing - across;
                m00 += across + (weightAlong * dx * dx); m01 += weightAlong * dx * dy; m02 += weightAlong * dx * dz;
                m11 += across + (weightAlong * dy * dy); m12 += weightAlong * dy * dz;
                m22 += across + (weightAlong * dz * dz);
                double aAlong = (a.x * dx) + (a.y * dy) + (a.z * dz);
                double middleAlong = (((a.x + b.x) * dx) + ((a.y + b.y) * dy) + ((a.z + b.z) * dz)) * 0.5;
                double k = (spacing * middleAlong) - aAlong;
                r0 += a.x + (k * dx);
                r1 += a.y + (k * dy);
                r2 += a.z + (k * dz);
            }
            double c00 = (m11 * m22) - (m12 * m12);
            double c01 = (m02 * m12) - (m01 * m22);
            double c02 = (m01 * m12) - (m02 * m11);
            double determinant = (m00 * c00) + (m01 * c01) + (m02 * c02);
            if (Math.Abs(determinant) < 1e-18)
            {
                return position;
            }
            double c11 = (m00 * m22) - (m02 * m02);
            double c12 = (m01 * m02) - (m00 * m12);
            double c22 = (m00 * m11) - (m01 * m01);
            double x = ((c00 * r0) + (c01 * r1) + (c02 * r2)) / determinant;
            double y = ((c01 * r0) + (c11 * r1) + (c12 * r2)) / determinant;
            double z = ((c02 * r0) + (c12 * r1) + (c22 * r2)) / determinant;
            return position + new Vector3((float)x, (float)y, (float)z);
        }

        /// <summary>The direction the triangles around a vertex face, with both sides of double-sided geometry counted the same way.</summary>
        internal static Vector3 FanNormal(int[] fan, int[] triangles, Vector3[] positions)
        {
            Vector3 sum = Vector3.zero;
            if (fan != null)
            {
                foreach (int first in fan)
                {
                    Vector3 a = positions[triangles[first]];
                    Vector3 normal = Vector3.Cross(positions[triangles[first + 1]] - a, positions[triangles[first + 2]] - a);
                    sum += Vector3.Dot(normal, sum) < 0f ? -normal : normal;
                }
            }
            return sum.sqrMagnitude > 1e-24f ? sum.normalized : Vector3.zero;
        }

        /// <summary>
        /// The point of the triangles around a vertex nearest to <paramref name="point"/>, the triangle it
        /// is on (its first index) and its weights on that triangle's three corners.
        /// </summary>
        internal static bool ClosestPointOnFan(
            Vector3 point, int[] fan, int[] triangles, Vector3[] positions,
            out Vector3 closest, out int triangle, out Vector3 barycentric)
        {
            closest = point;
            triangle = -1;
            barycentric = Vector3.zero;
            if (fan == null)
            {
                return false;
            }
            float best = float.PositiveInfinity;
            foreach (int first in fan)
            {
                Vector3 a = positions[triangles[first]], b = positions[triangles[first + 1]], c = positions[triangles[first + 2]];
                Vector3 candidate = ClosestPointOnTriangle(point, a, b, c);
                float distance = (candidate - point).sqrMagnitude;
                if (distance >= best)
                {
                    continue;
                }
                Vector3 ab = b - a, ac = c - a, ap = candidate - a;
                float d00 = Vector3.Dot(ab, ab), d01 = Vector3.Dot(ab, ac), d11 = Vector3.Dot(ac, ac);
                float d20 = Vector3.Dot(ap, ab), d21 = Vector3.Dot(ap, ac);
                float denominator = (d00 * d11) - (d01 * d01);
                if (denominator <= 1e-24f)
                {
                    continue;
                }
                float v = ((d11 * d20) - (d01 * d21)) / denominator;
                float w = ((d00 * d21) - (d01 * d20)) / denominator;
                best = distance;
                closest = candidate;
                triangle = first;
                barycentric = new Vector3(1f - v - w, v, w);
            }
            return triangle >= 0;
        }

        // ---- The stroke -------------------------------------------------------------

        private BeautifyTopology GetBeautifyTopology()
        {
            if (beautifyTopology != null && ReferenceEquals(beautifyTopologyState, activeSculptState) &&
                ReferenceEquals(beautifyTopologyMesh, BakedMesh) && beautifyTopology.group.Length == sculptSlotVertexCount)
            {
                return beautifyTopology;
            }
            beautifyTopology = null;
            if (BakedMesh == null || activeSculptState == null || sculptSlot == null || sculptSlotStart < 0)
            {
                return null;
            }
            int[] triangles = GetBakedTriangles();
            EnsureBrushTriangleCache(triangles);
            if (!brushTrianglesBySlot.TryGetValue(sculptSlot, out int[] slotTriangles))
            {
                return null;
            }
            var groupOf = new int[sculptSlotVertexCount];
            for (int i = 0; i < groupOf.Length; i++)
            {
                List<int> coincident = sculptCoincidentVertices != null ? sculptCoincidentVertices[i] : null;
                groupOf[i] = coincident != null && coincident.Count > 0 ? coincident[0] : i;
            }
            Vector2[] uvs = BakedMesh.uv;
            beautifyTopology = BuildBeautifyTopology(
                sculptSlotStart, sculptSlotVertexCount, slotTriangles, triangles, BakedMesh.vertices,
                uvs != null && uvs.Length == BakedMesh.vertexCount ? uvs : null, groupOf);
            beautifyTopologyState = activeSculptState;
            beautifyTopologyMesh = BakedMesh;
            return beautifyTopology;
        }

        /// <summary>Called when the samples of one mouse event start: reads the UVs Keep Texture will change.</summary>
        private void PrepareBeautifyEvent()
        {
            beautifyUVs = null;
            beautifyUVChanged = false;
            if (sculptTool != SculptTool.Beautify || sculptMaskTool != SculptMaskTool.None || !sculptBeautifyKeepTexture ||
                IsEditingBlendshape || BakedMesh == null || activeSculptState == null || !activeSculptState.uvScaleKnown)
            {
                return;
            }
            Vector2[] uvs = BakedMesh.uv;
            if (uvs != null && uvs.Length == BakedMesh.vertexCount)
            {
                beautifyUVs = uvs;
            }
        }

        /// <summary>Called before each sample: it reads the UVs the sample before it left.</summary>
        private void PrepareBeautifySample()
        {
            if (beautifyUVs == null)
            {
                return;
            }
            if (beautifyUVBefore == null || beautifyUVBefore.Length != beautifyUVs.Length)
            {
                beautifyUVBefore = new Vector2[beautifyUVs.Length];
            }
            Array.Copy(beautifyUVs, beautifyUVBefore, beautifyUVs.Length);
        }

        /// <summary>Writes the UVs Keep Texture changed, with the vertices of the same mouse event.</summary>
        private void CommitBeautifyUVs()
        {
            if (beautifyUVs != null && beautifyUVChanged && BakedMesh != null && beautifyUVs.Length == BakedMesh.vertexCount)
            {
                BakedMesh.uv = beautifyUVs;
                if (activeSculptState != null)
                {
                    activeSculptState.uvEdited = true;
                }
            }
            beautifyUVs = null;
            beautifyUVChanged = false;
        }

        /// <summary>
        /// Beautify on one welded vertex of the active slot: where it goes when it moves
        /// <paramref name="blend"/> of the way to its place among its neighbours, and the UV it takes
        /// there when Keep Texture is on. False when the vertex stays where it is.
        /// </summary>
        private bool TryBeautifyVertex(List<int> coincident, Vector3[] before, float blend, out Vector3 position, out Vector2 uv, out bool hasUV)
        {
            position = Vector3.zero;
            uv = Vector2.zero;
            hasUV = false;
            BeautifyTopology topology = GetBeautifyTopology();
            if (topology == null || coincident == null || coincident.Count == 0 || blend <= 0f)
            {
                return false;
            }
            int first = topology.group[coincident[0]];
            int[] ring = topology.ring[first];
            int[] fan = topology.fan[first];
            bool keepTexture = beautifyUVs != null && beautifyUVBefore != null;
            if (topology.open[first] || ring == null || ring.Length < 3 || ring.Length > BeautifyMaxRing || fan == null ||
                (keepTexture && topology.seam[first]))
            {
                return false;
            }
            for (int k = 0; k < ring.Length; k++)
            {
                beautifyRing[k] = before[sculptSlotStart + ring[k]];
            }
            Vector3 current = before[sculptSlotStart + first];
            Vector3 move = (BeautifyTarget(current, beautifyRing, ring.Length, sculptBeautifyEvenSpacing) - current) * blend;
            int[] triangles = GetBakedTriangles();
            if (sculptBeautifyKeepShape)
            {
                // Across the surface only: toward or away from it would change the shape.
                Vector3 normal = FanNormal(fan, triangles, before);
                move -= normal * Vector3.Dot(move, normal);
            }
            if (move.sqrMagnitude < 1e-16f)
            {
                return false;
            }
            position = current + move;
            bool onSurface = ClosestPointOnFan(position, fan, triangles, before, out Vector3 closest, out int triangle, out Vector3 weights);
            if (onSurface && sculptBeautifyKeepShape)
            {
                position = closest;
            }
            if (onSurface && keepTexture)
            {
                uv = (beautifyUVBefore[triangles[triangle]] * weights.x) +
                     (beautifyUVBefore[triangles[triangle + 1]] * weights.y) +
                     (beautifyUVBefore[triangles[triangle + 2]] * weights.z);
                hasUV = true;
            }
            return true;
        }

        // ---- Panel --------------------------------------------------------------------

        private void DrawBeautifyOptions()
        {
            EditorGUILayout.HelpBox(
                "Brush over crooked polygons: each vertex slides along the surface until it is in line with its neighbours on either side, " +
                "along the edge loops through it. Open edges, and the edges a slot shares with other slots, stay where they are. " +
                "Effect % sets how far a stroke goes; brush again, or use Constant Application, for more.",
                MessageType.None);
            sculptBeautifyEvenSpacing = EditorGUILayout.Slider(
                new GUIContent(
                    "Even Spacing",
                    "Also even out the distances between the vertices along each line. 0 keeps the spacing as modelled (closer loops at a joint stay closer) and only straightens."),
                sculptBeautifyEvenSpacing, 0f, 1f);
            sculptBeautifyKeepShape = EditorGUILayout.Toggle(
                new GUIContent(
                    "Keep Shape",
                    "Move vertices only across the surface, and keep them on it. Off: the lines are also straightened in depth, which flattens the surface under the brush."),
                sculptBeautifyKeepShape);
            sculptBeautifyKeepTexture = EditorGUILayout.Toggle(
                new GUIContent(
                    "Keep Texture",
                    "Give each vertex that moves the UV of the place it moved to, so the texture stays where it is on the surface. " +
                    "Vertices on UV seams are then left where they are. Off: the texture moves with the vertices. Not used while a blendshape is loaded."),
                sculptBeautifyKeepTexture);
        }
    }
}
