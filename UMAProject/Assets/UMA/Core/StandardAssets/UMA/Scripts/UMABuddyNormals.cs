using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Vertex normals from a mesh's shape, under UMA's rule for "battle buddies": vertices at one
    /// position are one point of the surface and get one normal. A mesh has such copies wherever a UV
    /// island ends, and wherever two slots connect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity's <c>Mesh.RecalculateNormals</c> works on vertex indices, so each buddy gets the normal of
    /// the faces on its own side only, and the light breaks along every UV seam and slot connection.
    /// Use this class wherever normals are worked out from positions.
    /// </para>
    /// <para>
    /// The one exception is geometry that is double sided by having a reversed copy at the same
    /// positions: the two sides face opposite ways, their normals cancel, and one normal cannot serve
    /// both. Buddies whose faces cancel (the sum is under <see cref="OpposedFraction"/> of its parts)
    /// are split into the two sides, and each side gets its own normal.
    /// </para>
    /// <para>
    /// A locked vertex is one whose normal may not be changed here (it belongs to a slot that is not
    /// being edited). Its buddies take its normal, so the connection still has one normal.
    /// </para>
    /// </remarks>
    public sealed class UMABuddyNormals
    {
        /// <summary>Buddies are split into two sides when their summed normal is shorter than this fraction of its parts.</summary>
        public const float OpposedFraction = 0.05f;

        private readonly int[] triangles;
        private readonly int[] groupOf;
        private readonly int[] groupStart;
        private readonly int[] groupMembers;
        private readonly int[] faceStart;
        private readonly int[] faces;
        private readonly List<int> side = new List<int>(8);
        private bool[] affected;

        public int VertexCount => groupOf.Length;
        public int GroupCount => groupStart.Length - 1;

        /// <summary>The buddy group of a vertex. Vertices with the same group are at one position.</summary>
        public int GroupOf(int vertex) => groupOf[vertex];

        /// <summary>How close two vertices must be to count as one position, for a mesh of this size.</summary>
        public static float DefaultTolerance(Vector3[] positions)
        {
            if (positions == null || positions.Length == 0)
            {
                return 0.000001f;
            }
            var bounds = new Bounds(positions[0], Vector3.zero);
            for (int i = 1; i < positions.Length; i++)
            {
                bounds.Encapsulate(positions[i]);
            }
            return Mathf.Max(0.000001f, bounds.size.magnitude * 0.000001f);
        }

        /// <param name="positions">The positions that decide which vertices are buddies.</param>
        /// <param name="triangles">Every triangle of the mesh, three indices each. Not copied: do not change it afterwards.</param>
        /// <param name="tolerance">How close two vertices must be to count as one position; zero or less for <see cref="DefaultTolerance"/>.</param>
        public UMABuddyNormals(Vector3[] positions, int[] triangles, float tolerance = 0f)
        {
            this.triangles = triangles ?? new int[0];
            int count = positions != null ? positions.Length : 0;
            if (tolerance <= 0f)
            {
                tolerance = DefaultTolerance(positions);
            }
            float inverse = 1f / tolerance;
            groupOf = new int[count];
            var cells = new Dictionary<Vector3Int, int>(count);
            int groups = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = positions[i];
                var cell = new Vector3Int(Mathf.RoundToInt(p.x * inverse), Mathf.RoundToInt(p.y * inverse), Mathf.RoundToInt(p.z * inverse));
                if (!cells.TryGetValue(cell, out int group))
                {
                    group = groups++;
                    cells.Add(cell, group);
                }
                groupOf[i] = group;
            }

            groupStart = new int[groups + 1];
            for (int i = 0; i < count; i++) groupStart[groupOf[i] + 1]++;
            for (int g = 0; g < groups; g++) groupStart[g + 1] += groupStart[g];
            groupMembers = new int[count];
            var fill = new int[groups];
            for (int i = 0; i < count; i++)
            {
                int g = groupOf[i];
                groupMembers[groupStart[g] + fill[g]++] = i;
            }

            faceStart = new int[count + 1];
            int triangleEnd = this.triangles.Length - this.triangles.Length % 3;
            for (int t = 0; t < triangleEnd; t += 3)
            {
                if (!ValidTriangle(t, count)) continue;
                faceStart[this.triangles[t] + 1]++;
                faceStart[this.triangles[t + 1] + 1]++;
                faceStart[this.triangles[t + 2] + 1]++;
            }
            for (int i = 0; i < count; i++) faceStart[i + 1] += faceStart[i];
            faces = new int[faceStart[count]];
            var faceFill = new int[count];
            for (int t = 0; t < triangleEnd; t += 3)
            {
                if (!ValidTriangle(t, count)) continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    int v = this.triangles[t + corner];
                    faces[faceStart[v] + faceFill[v]++] = t;
                }
            }
        }

        private bool ValidTriangle(int t, int count)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            return a >= 0 && b >= 0 && c >= 0 && a < count && b < count && c < count;
        }

        /// <summary>
        /// Works out normals from the shape. Every group of buddies gets one normal, from all the faces
        /// around the position. Vertices no face uses keep the normal they have.
        /// </summary>
        /// <param name="positions">The shape.</param>
        /// <param name="normals">The normals to write; also read for locked vertices.</param>
        /// <param name="weightByArea">True: larger faces count for more. False: every face counts once.</param>
        /// <param name="locked">Optional. Vertices whose normal stays; their buddies take it.</param>
        /// <returns>How many normals were written.</returns>
        public int Recalculate(Vector3[] positions, Vector3[] normals, bool weightByArea = false, bool[] locked = null)
        {
            int written = 0;
            for (int g = 0; g < GroupCount; g++)
            {
                int first = groupStart[g], end = groupStart[g + 1];
                if (TakeLockedNormal(first, end, normals, normals, locked, ref written))
                {
                    continue;
                }
                side.Clear();
                for (int m = first; m < end; m++) side.Add(groupMembers[m]);
                if (RecalculateSide(positions, normals, weightByArea, ref written))
                {
                    continue;
                }
                // The faces cancel: two sides of double-sided geometry. One normal for each side.
                Vector3 reference = Vector3.zero;
                for (int m = first; m < end && reference == Vector3.zero; m++)
                {
                    reference = VertexSum(groupMembers[m], positions, weightByArea, out _);
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    side.Clear();
                    for (int m = first; m < end; m++)
                    {
                        bool front = Vector3.Dot(VertexSum(groupMembers[m], positions, weightByArea, out _), reference) >= 0f;
                        if (front == (pass == 0)) side.Add(groupMembers[m]);
                    }
                    RecalculateSide(positions, normals, weightByArea, ref written);
                }
            }
            return written;
        }

        /// <summary>
        /// Makes normals follow a change of shape. Where the faces around a position turned, the normal
        /// there turns by the same amount, so normals that were set by hand or came with the slot keep
        /// their character; everywhere else nothing is written. Buddies end with one normal.
        /// </summary>
        /// <param name="before">The shape the <paramref name="beforeNormals"/> belong to.</param>
        /// <param name="after">The shape now.</param>
        /// <param name="beforeNormals">The normals before the change.</param>
        /// <param name="normals">The normals to write. Start it as a copy of <paramref name="beforeNormals"/>.</param>
        /// <param name="locked">Optional. Vertices whose normal stays; their buddies take it.</param>
        /// <returns>How many normals were written.</returns>
        public int Follow(Vector3[] before, Vector3[] after, Vector3[] beforeNormals, Vector3[] normals, bool[] locked = null)
        {
            if (affected == null) affected = new bool[GroupCount];
            else System.Array.Clear(affected, 0, affected.Length);
            int count = VertexCount;
            int triangleEnd = triangles.Length - triangles.Length % 3;
            bool any = false;
            for (int t = 0; t < triangleEnd; t += 3)
            {
                if (!ValidTriangle(t, count)) continue;
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (before[a] == after[a] && before[b] == after[b] && before[c] == after[c]) continue;
                affected[groupOf[a]] = affected[groupOf[b]] = affected[groupOf[c]] = true;
                any = true;
            }
            if (!any)
            {
                return 0;
            }

            int written = 0;
            for (int g = 0; g < GroupCount; g++)
            {
                if (!affected[g]) continue;
                int first = groupStart[g], end = groupStart[g + 1];
                if (TakeLockedNormal(first, end, beforeNormals, normals, locked, ref written))
                {
                    continue;
                }
                side.Clear();
                for (int m = first; m < end; m++) side.Add(groupMembers[m]);
                if (FollowSide(before, after, beforeNormals, normals, ref written))
                {
                    continue;
                }
                // Opposed buddies: the two sides of double-sided geometry each follow their own faces.
                Vector3 reference = beforeNormals[groupMembers[first]];
                for (int pass = 0; pass < 2; pass++)
                {
                    side.Clear();
                    for (int m = first; m < end; m++)
                    {
                        bool front = Vector3.Dot(beforeNormals[groupMembers[m]], reference) >= 0f;
                        if (front == (pass == 0)) side.Add(groupMembers[m]);
                    }
                    FollowSide(before, after, beforeNormals, normals, ref written);
                }
            }
            return written;
        }

        /// <summary>When a group has a locked vertex, gives its normal to the unlocked ones. True when the group is done.</summary>
        private bool TakeLockedNormal(int first, int end, Vector3[] source, Vector3[] normals, bool[] locked, ref int written)
        {
            if (locked == null)
            {
                return false;
            }
            int keeper = -1;
            for (int m = first; m < end && keeper < 0; m++)
            {
                if (locked[groupMembers[m]]) keeper = groupMembers[m];
            }
            if (keeper < 0)
            {
                return false;
            }
            Vector3 normal = source[keeper];
            for (int m = first; m < end; m++)
            {
                int v = groupMembers[m];
                if (!locked[v] && normals[v] != normal)
                {
                    normals[v] = normal;
                    written++;
                }
            }
            return true;
        }

        /// <summary>One normal for the vertices in <see cref="side"/>. False when their faces cancel.</summary>
        private bool RecalculateSide(Vector3[] positions, Vector3[] normals, bool weightByArea, ref int written)
        {
            if (side.Count == 0)
            {
                return true;
            }
            Vector3 sum = SideSum(positions, weightByArea, out float weight);
            if (weight <= 0f)
            {
                return true; // No face uses these vertices.
            }
            float length = sum.magnitude;
            if (length < OpposedFraction * weight)
            {
                return false;
            }
            Vector3 normal = sum / length;
            for (int i = 0; i < side.Count; i++)
            {
                normals[side[i]] = normal;
                written++;
            }
            return true;
        }

        /// <summary>Turns the shared normal of the vertices in <see cref="side"/> as their faces turned. False when they are opposed.</summary>
        private bool FollowSide(Vector3[] before, Vector3[] after, Vector3[] beforeNormals, Vector3[] normals, ref int written)
        {
            if (side.Count == 0)
            {
                return true;
            }
            Vector3 was = SideSum(before, false, out float wasWeight);
            Vector3 now = SideSum(after, false, out float nowWeight);
            if (wasWeight <= 0f || nowWeight <= 0f)
            {
                return true; // No face uses these vertices.
            }
            Vector3 shared = beforeNormals[side[0]];
            Vector3 normalSum = shared;
            bool same = true;
            for (int i = 1; i < side.Count; i++)
            {
                Vector3 normal = beforeNormals[side[i]];
                normalSum += normal;
                same &= normal == shared;
            }
            if (was.magnitude < OpposedFraction * wasWeight || now.magnitude < OpposedFraction * nowWeight ||
                normalSum.magnitude < OpposedFraction * side.Count)
            {
                return false;
            }
            if (!same)
            {
                shared = normalSum.normalized;
            }
            Vector3 wasDirection = was.normalized, nowDirection = now.normalized;
            Vector3 result = Vector3.Dot(wasDirection, nowDirection) > 0.9999999f
                ? shared
                : (Quaternion.FromToRotation(wasDirection, nowDirection) * shared).normalized;
            for (int i = 0; i < side.Count; i++)
            {
                int v = side[i];
                if (normals[v] != result)
                {
                    normals[v] = result;
                    written++;
                }
            }
            return true;
        }

        private Vector3 SideSum(Vector3[] positions, bool weightByArea, out float weight)
        {
            Vector3 sum = Vector3.zero;
            weight = 0f;
            for (int i = 0; i < side.Count; i++)
            {
                sum += VertexSum(side[i], positions, weightByArea, out float vertexWeight);
                weight += vertexWeight;
            }
            return sum;
        }

        /// <summary>The summed normals of the faces that use one vertex, and how much went into the sum.</summary>
        private Vector3 VertexSum(int vertex, Vector3[] positions, bool weightByArea, out float weight)
        {
            Vector3 sum = Vector3.zero;
            weight = 0f;
            for (int f = faceStart[vertex]; f < faceStart[vertex + 1]; f++)
            {
                int t = faces[f];
                Vector3 a = positions[triangles[t]];
                Vector3 cross = Vector3.Cross(positions[triangles[t + 1]] - a, positions[triangles[t + 2]] - a);
                float length = cross.magnitude;
                if (length < 1e-20f) continue;
                if (weightByArea)
                {
                    sum += cross;
                    weight += length;
                }
                else
                {
                    sum += cross / length;
                    weight += 1f;
                }
            }
            return sum;
        }
    }
}
