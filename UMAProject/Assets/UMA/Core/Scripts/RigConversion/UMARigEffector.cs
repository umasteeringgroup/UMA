using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>The volume an effector acts in.</summary>
    public enum UMARigEffectorShape
    {
        Sphere = 0,
        /// <summary>A cylinder with rounded ends, running along the effector's own y axis.</summary>
        Capsule = 1,
    }

    /// <summary>What an effector does to the vertices inside it.</summary>
    public enum UMARigEffectorAction
    {
        /// <summary>Push away from the sphere's centre, or from the capsule's centre line.</summary>
        Push = 0,
        /// <summary>Shift by a fixed offset.</summary>
        Move = 1,
        /// <summary>Scale about the effector's centre, along its own axes.</summary>
        Scale = 2,
        /// <summary>Bring cloth that lies under the skin, or too close to it, out to a clearance above it.</summary>
        ClearBody = 3,
    }

    /// <summary>
    /// A region of a rig conversion profile: every vertex of a converted slot that falls inside it is
    /// moved, fully near the middle and less toward the edge. Bone entries move whatever a bone
    /// carries; an effector moves a place, whatever it is weighted to.
    /// </summary>
    [Serializable]
    public sealed class UMARigEffector
    {
        public string name = "Effector";

        public bool enabled = true;

        public UMARigEffectorShape shape = UMARigEffectorShape.Sphere;

        [Tooltip("The bone of the target rig the effector is placed from. Empty places it from the character's origin.")]
        public string boneName;

        [Tooltip("Where the effector's centre is, in metres from the bone's rest joint, in avatar space.")]
        public Vector3 position;

        [Tooltip("How the effector is turned, in degrees, in avatar space. A capsule runs along its own y axis.")]
        public Vector3 rotationEuler;

        [Tooltip("Outer radius in metres. Nothing further from the centre (or the capsule's centre line) is touched.")]
        public float radius = 0.1f;

        [Tooltip("How much of the radius the effect fades over. 0 is full strength right up to a hard edge; 1 fades all the way from the centre.")]
        [Range(0f, 1f)]
        public float falloff = 0.6f;

        [Tooltip("Capsule: distance in metres between the centres of its two rounded ends.")]
        public float length = 0.2f;

        [Tooltip("Capsule: strength along its length, from one end (left of the curve) to the other (right). Use it to fade the effect in and out along the capsule.")]
        public AnimationCurve axisCurve = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Also apply the mirror image of this effector on the other side of the character.")]
        public bool mirror;

        public UMARigEffectorAction action = UMARigEffectorAction.Push;

        [Tooltip("Push: how far, in metres, at full strength. Clear the body: the gap to leave between skin and cloth.")]
        public float amount = 0.01f;

        [Tooltip("Move: the offset in metres, in avatar space, at full strength.")]
        public Vector3 move;

        [Tooltip("Scale: about the effector's centre, along its own axes. (1, 1, 1) changes nothing.")]
        public Vector3 scale = Vector3.one;

        [Tooltip("Clear the body: how far under the skin, in metres, cloth is still brought out. Cloth deeper than this is left alone.")]
        public float reach = 0.08f;

        [Tooltip("Clear the body: how many times the push is spread to neighbouring vertices, so the cloth that was moved blends into the cloth that was not.")]
        [Range(0, 8)]
        public int smoothing = 2;

        /// <summary>The scale to apply. A zero component counts as 1.</summary>
        public Vector3 SafeScale => new Vector3(scale.x == 0f ? 1f : scale.x, scale.y == 0f ? 1f : scale.y, scale.z == 0f ? 1f : scale.z);

        /// <summary>True when the effector is switched on and its settings can move anything.</summary>
        public bool IsActive
        {
            get
            {
                if (!enabled || radius <= 0f)
                {
                    return false;
                }
                switch (action)
                {
                    case UMARigEffectorAction.Push: return amount != 0f;
                    case UMARigEffectorAction.Move: return move != Vector3.zero;
                    case UMARigEffectorAction.Scale: return SafeScale != Vector3.one;
                    default: return true;
                }
            }
        }

        /// <summary>
        /// Where the effector sits on a rig at rest, in avatar space.
        /// </summary>
        /// <param name="mirrored">The mirror image across the character's left-right plane instead.</param>
        public void GetFrame(UMARigRestPose rig, bool mirrored, out Vector3 centre, out Quaternion rotation)
        {
            Vector3 anchor = Vector3.zero;
            if (rig != null && !string.IsNullOrEmpty(boneName))
            {
                rig.TryGetAvatarPosition(UMAUtils.StringToHash(boneName), out anchor);
            }
            centre = anchor + position;
            rotation = Quaternion.Euler(rotationEuler);
            if (mirrored)
            {
                centre = new Vector3(-centre.x, centre.y, centre.z);
                rotation = new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
            }
        }

        /// <summary>
        /// How strongly the effector acts on a point in avatar space, from 0 (outside) to 1 (in the
        /// core), including a capsule's curve along its length.
        /// </summary>
        public float GetWeight(Vector3 point, UMARigRestPose rig, bool mirrored = false)
        {
            return new Placed(this, rig, mirrored).Weight(point, out _);
        }

        /// <summary>
        /// How many of the points, in avatar space, stand inside the effector, or inside its mirror
        /// image when it is mirrored. Whether the effector is switched on does not matter: this is
        /// about where it is.
        /// </summary>
        public int CountInside(IReadOnlyList<Vector3> points, UMARigRestPose rig)
        {
            if (points == null || radius <= 0f)
            {
                return 0;
            }
            var placed = new Placed(this, rig, false);
            var image = new Placed(this, rig, true);
            int inside = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (placed.Weight(points[i], out _) > 0f || (mirror && image.Weight(points[i], out _) > 0f))
                {
                    inside++;
                }
            }
            return inside;
        }

        /// <summary>An effector placed on a rig, with everything a per-vertex test needs worked out once.</summary>
        internal readonly struct Placed
        {
            public readonly UMARigEffector Source;
            public readonly Vector3 Centre;
            public readonly Quaternion Rotation;
            public readonly Quaternion InverseRotation;
            public readonly Vector3 Axis;
            public readonly float HalfLength;
            public readonly float Radius;
            public readonly float Core;
            public readonly Vector3 Move;

            public Placed(UMARigEffector source, UMARigRestPose rig, bool mirrored)
            {
                Source = source;
                source.GetFrame(rig, mirrored, out Centre, out Rotation);
                InverseRotation = Quaternion.Inverse(Rotation);
                Axis = Rotation * Vector3.up;
                HalfLength = source.shape == UMARigEffectorShape.Capsule ? Mathf.Max(0f, source.length) * 0.5f : 0f;
                Radius = Mathf.Max(0f, source.radius);
                Core = Radius * (1f - Mathf.Clamp01(source.falloff));
                Move = mirrored ? new Vector3(-source.move.x, source.move.y, source.move.z) : source.move;
            }

            /// <summary>
            /// Strength at a point, and the point of the effector's centre line nearest to it (the
            /// centre itself for a sphere), which Push moves away from.
            /// </summary>
            public float Weight(Vector3 point, out Vector3 axisPoint)
            {
                axisPoint = Centre;
                float along = 0.5f;
                if (HalfLength > 0f)
                {
                    float s = Mathf.Clamp(Vector3.Dot(point - Centre, Axis), -HalfLength, HalfLength);
                    axisPoint = Centre + Axis * s;
                    along = (s + HalfLength) / (2f * HalfLength);
                }
                float distance = Vector3.Distance(point, axisPoint);
                if (distance >= Radius)
                {
                    return 0f;
                }
                float weight = 1f;
                if (distance > Core)
                {
                    float x = (Radius - distance) / (Radius - Core);
                    weight = x * x * (3f - 2f * x);
                }
                if (HalfLength > 0f && Source.axisCurve != null && Source.axisCurve.length > 0)
                {
                    weight *= Mathf.Max(0f, Source.axisCurve.Evaluate(along));
                }
                return weight;
            }
        }
    }

    /// <summary>What a set of effectors did to one slot.</summary>
    public sealed class UMARigEffectorReport
    {
        public int MovedVertices;
        public float MaxMove;
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// The skin of a character at rest, in avatar space, with a way to find the nearest point on it.
    /// "Clear the body" effectors measure cloth against this.
    /// </summary>
    public sealed class UMARigBodySurface
    {
        private const float CellSize = 0.02f;
        private const float TieTolerance = 1e-10f;  // squared metres: nearer than this to the best is the same point

        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        // The triangles of each grid cell, packed: cell c holds cellItems[cellStart[c] .. cellStart[c + 1]).
        // Built on the first search after the last Add.
        private bool gridBuilt;
        private Vector3 gridMin;
        private int gridX, gridY, gridZ;
        private int[] cellStart = new int[1];
        private int[] cellItems = new int[0];
        private int[] visited = new int[0];
        private int visit;

        public int TriangleCount => triangles.Count / 3;

        public int VertexCount => positions.Count;

        /// <summary>
        /// The skin made of the given slots, each skinned to avatar space with the rig at rest. Slots
        /// that are not skinned or have no normals are skipped.
        /// </summary>
        public static UMARigBodySurface FromSlots(IEnumerable<SlotDataAsset> slots, UMARigRestPose rig)
        {
            var surface = new UMARigBodySurface();
            if (slots == null || rig == null)
            {
                return surface;
            }
            foreach (SlotDataAsset slot in slots)
            {
                if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.submeshes == null)
                {
                    continue;
                }
                if (!TryBakeToAvatar(slot.meshData, rig, out Vector3[] slotPositions, out Vector3[] slotNormals))
                {
                    continue;
                }
                for (int s = 0; s < slot.meshData.submeshes.Length; s++)
                {
                    if (slot.meshData.submeshes[s] != null)
                    {
                        surface.Add(slotPositions, slotNormals, slot.meshData.submeshes[s].getManagedTriangles(0));
                    }
                }
            }
            return surface;
        }

        /// <summary>Adds a mesh in avatar space. Degenerate triangles are skipped.</summary>
        public void Add(Vector3[] meshPositions, Vector3[] meshNormals, int[] meshTriangles)
        {
            if (meshPositions == null || meshNormals == null || meshTriangles == null || meshNormals.Length != meshPositions.Length)
            {
                return;
            }
            int start = positions.Count;
            positions.AddRange(meshPositions);
            normals.AddRange(meshNormals);
            for (int t = 0; t + 2 < meshTriangles.Length; t += 3)
            {
                int i0 = meshTriangles[t], i1 = meshTriangles[t + 1], i2 = meshTriangles[t + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= meshPositions.Length || i1 >= meshPositions.Length || i2 >= meshPositions.Length)
                {
                    continue;
                }
                Vector3 a = meshPositions[i0], b = meshPositions[i1], c = meshPositions[i2];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-16f)
                {
                    continue;
                }
                triangles.Add(start + i0);
                triangles.Add(start + i1);
                triangles.Add(start + i2);
            }
            gridBuilt = false;
        }

        /// <summary>Sorts the triangles into a grid of cells over the skin's bounds, so a search only meets the ones nearby.</summary>
        private void BuildGrid()
        {
            gridBuilt = true;
            int triangleCount = triangles.Count / 3;
            if (triangleCount == 0)
            {
                gridX = gridY = gridZ = 0;
                return;
            }
            Vector3 min = positions[triangles[0]], max = min;
            for (int i = 0; i < triangles.Count; i++)
            {
                Vector3 p = positions[triangles[i]];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            gridMin = min;
            gridX = Mathf.Clamp(Mathf.FloorToInt((max.x - min.x) / CellSize) + 1, 1, 512);
            gridY = Mathf.Clamp(Mathf.FloorToInt((max.y - min.y) / CellSize) + 1, 1, 512);
            gridZ = Mathf.Clamp(Mathf.FloorToInt((max.z - min.z) / CellSize) + 1, 1, 512);

            // Count, then fill: two passes over the triangles and no per-cell lists.
            cellStart = new int[gridX * gridY * gridZ + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int t = 0; t < triangleCount; t++)
                {
                    Vector3 a = positions[triangles[t * 3]], b = positions[triangles[t * 3 + 1]], c = positions[triangles[t * 3 + 2]];
                    Vector3 low = Vector3.Min(a, Vector3.Min(b, c));
                    Vector3 high = Vector3.Max(a, Vector3.Max(b, c));
                    int x0 = CellX(low.x), x1 = CellX(high.x), y0 = CellY(low.y), y1 = CellY(high.y), z0 = CellZ(low.z), z1 = CellZ(high.z);
                    for (int x = x0; x <= x1; x++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            for (int z = z0; z <= z1; z++)
                            {
                                int cell = (x * gridY + y) * gridZ + z;
                                if (pass == 0)
                                {
                                    cellStart[cell + 1]++;
                                }
                                else
                                {
                                    cellItems[cellStart[cell]++] = t;
                                }
                            }
                        }
                    }
                }
                if (pass == 0)
                {
                    for (int cell = 0; cell < cellStart.Length - 1; cell++)
                    {
                        cellStart[cell + 1] += cellStart[cell];
                    }
                    cellItems = new int[cellStart[cellStart.Length - 1]];
                }
                else
                {
                    // Filling moved every start to its cell's end; shift them back.
                    for (int cell = cellStart.Length - 1; cell > 0; cell--)
                    {
                        cellStart[cell] = cellStart[cell - 1];
                    }
                    cellStart[0] = 0;
                }
            }
        }

        private int CellX(float value) => Mathf.Clamp(Mathf.FloorToInt((value - gridMin.x) / CellSize), 0, gridX - 1);
        private int CellY(float value) => Mathf.Clamp(Mathf.FloorToInt((value - gridMin.y) / CellSize), 0, gridY - 1);
        private int CellZ(float value) => Mathf.Clamp(Mathf.FloorToInt((value - gridMin.z) / CellSize), 0, gridZ - 1);

        /// <summary>
        /// The point of the skin nearest to <paramref name="point"/>, when one lies within
        /// <paramref name="maxDistance"/>, and the skin's outward normal there (blended from the
        /// triangle's vertex normals, so it turns smoothly across the surface).
        /// </summary>
        public bool TryGetClosest(Vector3 point, float maxDistance, out Vector3 closest, out Vector3 normal)
        {
            closest = point;
            normal = Vector3.up;
            if (triangles.Count == 0 || maxDistance <= 0f)
            {
                return false;
            }
            if (!gridBuilt)
            {
                BuildGrid();
            }
            if (visited.Length < triangles.Count / 3)
            {
                visited = new int[triangles.Count / 3];
                visit = 0;
            }
            visit++;
            int stamp = visit;
            float best = maxDistance * maxDistance;
            bool found = false;
            Vector3 normalSum = Vector3.zero;
            // The cell the point is in; it may lie outside the grid, beside the skin's bounds.
            float fx = (point.x - gridMin.x) / CellSize, fy = (point.y - gridMin.y) / CellSize, fz = (point.z - gridMin.z) / CellSize;
            int cx = Mathf.FloorToInt(fx), cy = Mathf.FloorToInt(fy), cz = Mathf.FloorToInt(fz);
            // How far the point is from the nearest wall of its own cell: the least distance to anything
            // in the ring of cells around it. Each further ring is one cell further still.
            float toWall = Mathf.Min(Mathf.Min(fx - cx, cx + 1 - fx), Mathf.Min(Mathf.Min(fy - cy, cy + 1 - fy), Mathf.Min(fz - cz, cz + 1 - fz))) * CellSize;
            int rings = Mathf.CeilToInt(maxDistance / CellSize);
            for (int ring = 0; ring <= rings; ring++)
            {
                // Stop once nothing in this ring, or any beyond it, can be nearer than the best so far.
                float nearestPossible = toWall + (ring - 1) * CellSize;
                if (ring > 0 && nearestPossible * nearestPossible > best)
                {
                    break;
                }
                int xLow = Mathf.Max(cx - ring, 0), xHigh = Mathf.Min(cx + ring, gridX - 1);
                int yLow = Mathf.Max(cy - ring, 0), yHigh = Mathf.Min(cy + ring, gridY - 1);
                int zLow = Mathf.Max(cz - ring, 0), zHigh = Mathf.Min(cz + ring, gridZ - 1);
                for (int x = xLow; x <= xHigh; x++)
                {
                    for (int y = yLow; y <= yHigh; y++)
                    {
                        bool shellXY = Mathf.Abs(x - cx) == ring || Mathf.Abs(y - cy) == ring;
                        for (int z = zLow; z <= zHigh; z++)
                        {
                            if (!shellXY && Mathf.Abs(z - cz) != ring)
                            {
                                continue;   // inside the ring: visited by a smaller one
                            }
                            int cell = (x * gridY + y) * gridZ + z;
                            int last = cellStart[cell + 1];
                            for (int i = cellStart[cell]; i < last; i++)
                            {
                                int triangle = cellItems[i];
                                if (visited[triangle] == stamp)
                                {
                                    continue;
                                }
                                visited[triangle] = stamp;
                                int i0 = triangles[triangle * 3], i1 = triangles[triangle * 3 + 1], i2 = triangles[triangle * 3 + 2];
                                Vector3 candidate = ClosestOnTriangle(point, positions[i0], positions[i1], positions[i2], out float u, out float v, out float w);
                                float distance = (candidate - point).sqrMagnitude;
                                if (distance > best + TieTolerance)
                                {
                                    continue;
                                }
                                Vector3 blended = normals[i0] * u + normals[i1] * v + normals[i2] * w;
                                if (blended.sqrMagnitude < 1e-12f)
                                {
                                    continue;
                                }
                                blended.Normalize();
                                if (found && distance >= best - TieTolerance)
                                {
                                    // The same point on an edge or corner shared by several triangles. Where
                                    // two body slots meet, their vertices carry different normals; blending
                                    // them makes the answer the same whichever triangle is met first.
                                    normalSum += blended;
                                    best = Mathf.Min(best, distance);
                                }
                                else
                                {
                                    best = distance;
                                    closest = candidate;
                                    normalSum = blended;
                                    found = true;
                                }
                            }
                        }
                    }
                }
            }
            if (found)
            {
                normal = normalSum.sqrMagnitude > 1e-12f ? normalSum.normalized : Vector3.up;
            }
            return found;
        }

        /// <summary>
        /// Skins mesh data into avatar space with the rig at rest: every vertex through the bind poses
        /// of its own influences. Slots bound to the same rig then share one space, however each was
        /// exported. Returns false for mesh data that is not skinned or has no normals.
        /// </summary>
        public static bool TryBakeToAvatar(UMAMeshData mesh, UMARigRestPose rig, out Vector3[] bakedPositions, out Vector3[] bakedNormals)
        {
            bakedPositions = null;
            bakedNormals = null;
            if (rig == null || !UMASlotRigConverter.CanConvert(mesh, out _) || mesh.normals == null || mesh.normals.Length != mesh.vertices.Length)
            {
                return false;
            }

            UMARigRestPose full = rig.WithAdditionalBones(mesh.umaBones);
            int influenceCount = Mathf.Min(mesh.bindPoses.Length, mesh.boneNameHashes.Length);
            var skin = new Matrix4x4[influenceCount];
            for (int i = 0; i < influenceCount; i++)
            {
                if (!full.TryGetAvatarMatrix(mesh.boneNameHashes[i], out Matrix4x4 boneToAvatar))
                {
                    boneToAvatar = full.RigRootToAvatar;
                }
                skin[i] = boneToAvatar * mesh.bindPoses[i];
            }
            Skin(mesh, skin, full.RigRootToAvatar, out bakedPositions, out bakedNormals);
            return true;
        }

        /// <summary>Blends each vertex through its influences' matrices; a vertex with no weights uses <paramref name="unweighted"/>.</summary>
        public static void Skin(UMAMeshData mesh, Matrix4x4[] skin, Matrix4x4 unweighted, out Vector3[] skinnedPositions, out Vector3[] skinnedNormals)
        {
            int influenceCount = skin.Length;
            int count = mesh.vertices.Length;
            skinnedPositions = new Vector3[count];
            skinnedNormals = new Vector3[count];
            bool managed = mesh.ManagedBonesPerVertex != null && mesh.ManagedBonesPerVertex.Length == count &&
                           mesh.ManagedBoneWeights != null && mesh.ManagedBoneWeights.Length > 0;
            int cursor = 0;
            for (int v = 0; v < count; v++)
            {
                Matrix4x4 blended = default;
                float total = 0f;
                if (managed)
                {
                    int influences = mesh.ManagedBonesPerVertex[v];
                    for (int k = 0; k < influences; k++)
                    {
                        BoneWeight1 weight = mesh.ManagedBoneWeights[cursor++];
                        if (weight.boneIndex >= 0 && weight.boneIndex < influenceCount)
                        {
                            AddScaled(ref blended, skin[weight.boneIndex], weight.weight);
                            total += weight.weight;
                        }
                    }
                }
                else
                {
                    UMABoneWeight weight = mesh.boneWeights[v];
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex0, weight.weight0);
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex1, weight.weight1);
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex2, weight.weight2);
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex3, weight.weight3);
                }
                if (total <= 1e-6f)
                {
                    blended = unweighted;
                }
                skinnedPositions[v] = blended.MultiplyPoint3x4(mesh.vertices[v]);
                skinnedNormals[v] = blended.MultiplyVector(mesh.normals[v]).normalized;
            }
        }

        private static void AddLegacy(ref Matrix4x4 blended, ref float total, Matrix4x4[] skin, int boneIndex, float weight)
        {
            if (weight > 0f && boneIndex >= 0 && boneIndex < skin.Length)
            {
                AddScaled(ref blended, skin[boneIndex], weight);
                total += weight;
            }
        }

        private static void AddScaled(ref Matrix4x4 accumulator, Matrix4x4 matrix, float weight)
        {
            for (int i = 0; i < 16; i++)
            {
                accumulator[i] += matrix[i] * weight;
            }
        }

        /// <summary>The point of triangle abc nearest to p, with its barycentric weights for a, b and c.</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { u = 1f; v = 0f; w = 0f; return a; }

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { u = 0f; v = 1f; w = 0f; return b; }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float t = d1 / (d1 - d3);
                u = 1f - t; v = t; w = 0f;
                return a + ab * t;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { u = 0f; v = 0f; w = 1f; return c; }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float t = d2 / (d2 - d6);
                u = 1f - t; v = 0f; w = t;
                return a + ac * t;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                u = 0f; v = 1f - t; w = t;
                return b + (c - b) * t;
            }

            float denominator = 1f / (va + vb + vc);
            v = vb * denominator;
            w = vc * denominator;
            u = 1f - v - w;
            return a + ab * v + ac * w;
        }
    }

    /// <summary>Applies a profile's effectors to a slot that has been brought to the target rig.</summary>
    public static class UMARigEffectorDeformer
    {
        private const float WeldGrid = 10000f;   // vertices within 0.1 mm of each other move as one
        private const int ClearSteps = 4;        // how often "clear the body" looks again after a push
        private const float MinimumMove = 1e-5f; // metres; a vertex moved less than this is not moved at all
        private const int SurfacePasses = 2;     // how often the cloth between the vertices is tested and lifted
        private const float SurfaceSampleSpacing = 0.025f;  // metres between the points tested on a triangle
        private const float SurfaceDepth = 0.04f;           // metres: how far under the skin the flat of a triangle is still lifted out

        /// <summary>
        /// Moves the vertices the effectors reach, in order, each working on what the one before left.
        /// Normals and tangents are turned by the same amount the surface under them turned, so
        /// authored hard edges and seams keep their look. Returns true when anything moved.
        /// </summary>
        /// <param name="rig">The target rig at rest; effectors are placed from its bones.</param>
        /// <param name="body">The skin, for "clear the body" effectors. May be null: those effectors are then skipped with a warning.</param>
        /// <param name="positions">Avatar space, changed in place.</param>
        /// <param name="normals">Avatar space, changed in place. May be null.</param>
        /// <param name="tangents">Avatar space, changed in place. May be null.</param>
        /// <param name="triangles">The slot's triangle lists, one per submesh.</param>
        public static bool Apply(IReadOnlyList<UMARigEffector> effectors, UMARigRestPose rig, UMARigBodySurface body,
            Vector3[] positions, Vector3[] normals, Vector4[] tangents, IReadOnlyList<int[]> triangles, UMARigEffectorReport report = null)
        {
            if (effectors == null || positions == null || positions.Length == 0)
            {
                return false;
            }
            bool anyActive = false;
            for (int i = 0; i < effectors.Count; i++)
            {
                anyActive |= effectors[i] != null && effectors[i].IsActive;
            }
            if (!anyActive)
            {
                return false;
            }

            bool turnDirections = (normals != null || tangents != null) && triangles != null;
            var original = (Vector3[])positions.Clone();
            Topology topology = null;
            bool warnedNoBody = false;

            for (int i = 0; i < effectors.Count; i++)
            {
                UMARigEffector effector = effectors[i];
                if (effector == null || !effector.IsActive)
                {
                    continue;
                }
                for (int side = 0; side < (effector.mirror ? 2 : 1); side++)
                {
                    var placed = new UMARigEffector.Placed(effector, rig, side == 1);
                    if (effector.action == UMARigEffectorAction.ClearBody)
                    {
                        if (body == null || body.TriangleCount == 0)
                        {
                            if (!warnedNoBody)
                            {
                                report?.Warnings.Add($"Effector '{effector.name}' clears the body, but no body surface was given; it was skipped.");
                                warnedNoBody = true;
                            }
                            continue;
                        }
                        if (!ReachesAny(placed, positions))
                        {
                            // An effector for all slots meets many slots that are nowhere near it;
                            // those cost one pass over their vertices and nothing more.
                            continue;
                        }
                        if (topology == null)
                        {
                            topology = new Topology(original, triangles);
                        }
                        ClearBody(placed, body, positions, topology);
                    }
                    else
                    {
                        ApplySimple(placed, positions);
                    }
                }
            }

            int moved = 0;
            float maxMove = 0f;
            for (int v = 0; v < positions.Length; v++)
            {
                float distance = (positions[v] - original[v]).sqrMagnitude;
                if (distance < MinimumMove * MinimumMove)
                {
                    // Too little to matter (the far edge of a falloff): the vertex stays exactly where it was.
                    positions[v] = original[v];
                }
                else
                {
                    moved++;
                    maxMove = Mathf.Max(maxMove, distance);
                }
            }
            if (report != null)
            {
                report.MovedVertices = moved;
                report.MaxMove = Mathf.Sqrt(maxMove);
            }
            if (moved == 0)
            {
                return false;
            }

            if (turnDirections)
            {
                if (topology == null)
                {
                    topology = new Topology(original, triangles);
                }
                TurnDirections(topology, original, positions, normals, tangents);
            }
            return true;
        }

        /// <summary>True when the effector acts on at least one of the points.</summary>
        private static bool ReachesAny(UMARigEffector.Placed placed, Vector3[] positions)
        {
            for (int v = 0; v < positions.Length; v++)
            {
                if (placed.Weight(positions[v], out _) > 0f)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ApplySimple(UMARigEffector.Placed placed, Vector3[] positions)
        {
            UMARigEffector effector = placed.Source;
            Vector3 scale = effector.SafeScale;
            for (int v = 0; v < positions.Length; v++)
            {
                Vector3 p = positions[v];
                float weight = placed.Weight(p, out Vector3 axisPoint);
                if (weight <= 0f)
                {
                    continue;
                }
                switch (effector.action)
                {
                    case UMARigEffectorAction.Push:
                    {
                        Vector3 away = p - axisPoint;
                        float distance = away.magnitude;
                        if (distance > 1e-6f)
                        {
                            positions[v] = p + away / distance * (effector.amount * weight);
                        }
                        break;
                    }
                    case UMARigEffectorAction.Move:
                        positions[v] = p + placed.Move * weight;
                        break;
                    case UMARigEffectorAction.Scale:
                    {
                        Vector3 local = placed.InverseRotation * (p - placed.Centre);
                        Vector3 scaled = placed.Centre + placed.Rotation * Vector3.Scale(local, scale);
                        positions[v] = Vector3.LerpUnclamped(p, scaled, weight);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Brings cloth that lies under the skin, or closer to it than the clearance, out along the
        /// skin's normal. Vertices come first; then the surface between them, because a coarse garment
        /// whose vertices are all clear can still cut through a rounded body with the flat of its
        /// triangles. The push is then spread to neighbouring vertices so that moved cloth blends into
        /// unmoved cloth; no vertex ends up pushed less than it needs.
        /// </summary>
        private static void ClearBody(UMARigEffector.Placed placed, UMARigBodySurface body, Vector3[] positions, Topology topology)
        {
            UMARigEffector effector = placed.Source;
            float clearance = Mathf.Max(0f, effector.amount);
            float reach = Mathf.Max(0f, effector.reach);
            float search = reach + clearance + 0.02f;
            int groupCount = topology.GroupCount;
            var weight = new float[groupCount];
            var current = new Vector3[groupCount];
            var start = new Vector3[groupCount];
            var skin = new Vector3[groupCount];     // which way is out of the skin near each vertex
            var outOfReach = new bool[groupCount];  // further under the skin than the effector reaches, or nowhere near it: left alone
            var gapAfter = new float[groupCount];   // how far over the skin the vertex stands once it has been moved
            float furthest = reach + clearance + 0.02f;
            bool any = false;

            // 1. Vertices: out along the skin's normal to the clearance. Where two stretches of skin meet
            // (between the legs, under an arm) clearing one can leave the cloth under the other, so look
            // again from where it landed, a few times.
            for (int g = 0; g < groupCount; g++)
            {
                Vector3 p = positions[topology.FirstVertex[g]];
                start[g] = p;
                current[g] = p;
                float w = placed.Weight(p, out _);
                if (w <= 0f)
                {
                    continue;
                }
                weight[g] = w;
                Vector3 probe = p;
                Vector3 total = Vector3.zero;
                gapAfter[g] = clearance;
                for (int step = 0; step < ClearSteps; step++)
                {
                    if (!body.TryGetClosest(probe, search, out Vector3 closest, out Vector3 normal))
                    {
                        outOfReach[g] |= step == 0;
                        break;
                    }
                    if (step == 0)
                    {
                        skin[g] = normal;
                    }
                    float gap = Vector3.Dot(probe - closest, normal);
                    if (gap < -reach)
                    {
                        outOfReach[g] |= step == 0;
                        break;
                    }
                    if (gap >= clearance - 1e-5f)
                    {
                        gapAfter[g] = gap;
                        break;   // clear already
                    }
                    Vector3 push = normal * (clearance - gap);
                    if ((total + push).magnitude > furthest)
                    {
                        break;   // skin on every side: going further would only trade one for another
                    }
                    total += push;
                    probe += push;
                }
                if (total.sqrMagnitude > 1e-14f)
                {
                    current[g] = p + total * w;
                    any = true;
                }
            }

            // 2. The surface between the vertices: points spread over each triangle are tested the same
            // way, and a point found under the skin lifts its triangle's three vertices.
            int[] triangles = topology.GroupTriangles;
            var lift = new float[groupCount];
            var liftDirection = new Vector3[groupCount];
            var liftedSoFar = new float[groupCount];
            var liftedLastPass = new bool[groupCount];
            float surfaceSearch = clearance + SurfaceDepth;
            for (int pass = 0; pass < SurfacePasses && triangles.Length > 0; pass++)
            {
                bool lifted = false;
                Array.Clear(lift, 0, groupCount);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int ga = triangles[t], gb = triangles[t + 1], gc = triangles[t + 2];
                    if (weight[ga] <= 0f && weight[gb] <= 0f && weight[gc] <= 0f)
                    {
                        continue;
                    }
                    if (outOfReach[ga] || outOfReach[gb] || outOfReach[gc])
                    {
                        continue;   // a triangle that dives deeper than the reach, or stands well away, is left as it is
                    }
                    if (pass > 0 && !liftedLastPass[ga] && !liftedLastPass[gb] && !liftedLastPass[gc])
                    {
                        continue;   // nothing about this triangle has changed since it was last found clear
                    }
                    Vector3 a = current[ga], b = current[gb], c = current[gc];
                    float longest = Mathf.Sqrt(Mathf.Max((b - a).sqrMagnitude, Mathf.Max((c - b).sqrMagnitude, (a - c).sqrMagnitude)));
                    if (pass == 0 && Mathf.Min(gapAfter[ga], Mathf.Min(gapAfter[gb], gapAfter[gc])) > longest * 0.5f + clearance)
                    {
                        continue;   // every corner stands further off than the triangle could sag
                    }
                    int steps = Mathf.Clamp(Mathf.CeilToInt(longest / SurfaceSampleSpacing), 2, 4);
                    for (int i = 0; i <= steps; i++)
                    {
                        for (int j = 0; j <= steps - i; j++)
                        {
                            int k = steps - i - j;
                            if (i == steps || j == steps || k == steps)
                            {
                                continue;   // the corners are the vertices, done already
                            }
                            Vector3 sample = (a * i + b * j + c * k) / steps;
                            if (!body.TryGetClosest(sample, surfaceSearch, out Vector3 closest, out Vector3 normal))
                            {
                                continue;
                            }
                            float gap = Vector3.Dot(sample - closest, normal);
                            if (gap < -SurfaceDepth || gap >= clearance - 1e-4f)
                            {
                                continue;
                            }
                            // No vertex is lifted further by this test than a triangle can plausibly sag.
                            float need = clearance - gap;
                            lifted |= Raise(ga, Mathf.Min(need * weight[ga], surfaceSearch - liftedSoFar[ga]), normal, lift, liftDirection);
                            lifted |= Raise(gb, Mathf.Min(need * weight[gb], surfaceSearch - liftedSoFar[gb]), normal, lift, liftDirection);
                            lifted |= Raise(gc, Mathf.Min(need * weight[gc], surfaceSearch - liftedSoFar[gc]), normal, lift, liftDirection);
                        }
                    }
                }
                if (!lifted)
                {
                    break;
                }
                for (int g = 0; g < groupCount; g++)
                {
                    liftedLastPass[g] = lift[g] > 0f;
                    if (lift[g] > 0f)
                    {
                        current[g] += liftDirection[g] * lift[g];
                        liftedSoFar[g] += lift[g];
                        any = true;
                    }
                }
            }
            if (!any)
            {
                return;
            }

            // How far each vertex goes, and which way.
            var need2 = new float[groupCount];
            var direction = new Vector3[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                Vector3 moved = current[g] - start[g];
                float distance = moved.magnitude;
                if (distance > 1e-7f)
                {
                    need2[g] = distance;
                    direction[g] = moved / distance;
                }
                else
                {
                    direction[g] = skin[g];
                }
            }

            // 3. Spread the push outward, ring by ring. A vertex keeps at least what it needs itself.
            if (effector.smoothing > 0 && topology.HasNeighbours)
            {
                var spread = (float[])need2.Clone();
                var next = new float[groupCount];
                for (int pass = 0; pass < effector.smoothing; pass++)
                {
                    for (int g = 0; g < groupCount; g++)
                    {
                        if (weight[g] <= 0f)
                        {
                            next[g] = 0f;
                            continue;
                        }
                        int first = topology.NeighbourStart[g], last = topology.NeighbourStart[g + 1];
                        float sum = 0f;
                        for (int n = first; n < last; n++)
                        {
                            sum += spread[topology.Neighbours[n]];
                        }
                        float average = last > first ? sum / (last - first) : spread[g];
                        next[g] = Mathf.Max(need2[g], Mathf.Lerp(spread[g], average, 0.5f) * Mathf.Min(1f, weight[g] * 2f));
                    }
                    float[] swap = spread;
                    spread = next;
                    next = swap;
                }
                need2 = spread;

                // Vertices that were not near the skin borrow the direction their neighbours move in.
                for (int g = 0; g < groupCount; g++)
                {
                    if (need2[g] <= 0f || direction[g] != Vector3.zero)
                    {
                        continue;
                    }
                    Vector3 sum = Vector3.zero;
                    for (int n = topology.NeighbourStart[g]; n < topology.NeighbourStart[g + 1]; n++)
                    {
                        sum += direction[topology.Neighbours[n]];
                    }
                    direction[g] = sum.sqrMagnitude > 1e-12f ? sum.normalized : Vector3.zero;
                }
            }

            for (int v = 0; v < positions.Length; v++)
            {
                int g = topology.Group[v];
                if (need2[g] > 0f && direction[g] != Vector3.zero)
                {
                    positions[v] += direction[g] * need2[g];
                }
            }
        }

        /// <summary>Records that a vertex has to rise by at least <paramref name="amount"/>; the largest request wins.</summary>
        private static bool Raise(int group, float amount, Vector3 normal, float[] lift, Vector3[] liftDirection)
        {
            if (amount <= lift[group] || amount < 1e-6f)
            {
                return false;
            }
            lift[group] = amount;
            liftDirection[group] = normal;
            return true;
        }

        /// <summary>
        /// Turns each normal and tangent by the rotation that takes the surface's own direction at
        /// that vertex before the effectors to its direction after them.
        /// </summary>
        private static void TurnDirections(Topology topology, Vector3[] before, Vector3[] after, Vector3[] normals, Vector4[] tangents)
        {
            Vector3[] surfaceBefore = topology.SurfaceNormals(before);
            Vector3[] surfaceAfter = topology.SurfaceNormals(after);
            var turn = new Quaternion[topology.GroupCount];
            var turns = new bool[topology.GroupCount];
            for (int g = 0; g < turn.Length; g++)
            {
                Vector3 a = surfaceBefore[g], b = surfaceAfter[g];
                if (a == Vector3.zero || b == Vector3.zero || Vector3.Dot(a, b) > 0.9999999f)
                {
                    continue;
                }
                turn[g] = Quaternion.FromToRotation(a, b);
                turns[g] = true;
            }
            int count = after.Length;
            for (int v = 0; v < count; v++)
            {
                int g = topology.Group[v];
                if (!turns[g])
                {
                    continue;
                }
                if (normals != null)
                {
                    normals[v] = (turn[g] * normals[v]).normalized;
                }
                if (tangents != null)
                {
                    Vector4 t = tangents[v];
                    Vector3 turned = (turn[g] * new Vector3(t.x, t.y, t.z)).normalized;
                    tangents[v] = new Vector4(turned.x, turned.y, turned.z, t.w);
                }
            }
        }

        /// <summary>
        /// A slot's surface with vertices that share a position treated as one: which vertices belong
        /// together, which are joined by an edge, and the surface direction at each.
        /// </summary>
        private sealed class Topology
        {
            public readonly int[] Group;
            public readonly int[] FirstVertex;
            public readonly int GroupCount;
            public readonly int[] NeighbourStart;
            public readonly int[] Neighbours;
            /// <summary>Every triangle as three welded-vertex indices, one after another.</summary>
            public readonly int[] GroupTriangles;
            private readonly IReadOnlyList<int[]> triangles;

            public bool HasNeighbours => Neighbours.Length > 0;

            public Topology(Vector3[] positions, IReadOnlyList<int[]> triangles)
            {
                this.triangles = triangles;
                int count = positions.Length;
                Group = new int[count];
                var first = new List<int>(count);
                var byPosition = new Dictionary<(int, int, int), int>(count);
                for (int v = 0; v < count; v++)
                {
                    Vector3 p = positions[v];
                    var key = (Mathf.RoundToInt(p.x * WeldGrid), Mathf.RoundToInt(p.y * WeldGrid), Mathf.RoundToInt(p.z * WeldGrid));
                    if (!byPosition.TryGetValue(key, out int group))
                    {
                        group = first.Count;
                        first.Add(v);
                        byPosition.Add(key, group);
                    }
                    Group[v] = group;
                }
                FirstVertex = first.ToArray();
                GroupCount = FirstVertex.Length;

                var edges = new HashSet<long>();
                var degree = new int[GroupCount + 1];
                var groupTriangles = new List<int>();
                if (triangles != null)
                {
                    for (int s = 0; s < triangles.Count; s++)
                    {
                        int[] list = triangles[s];
                        if (list == null)
                        {
                            continue;
                        }
                        for (int t = 0; t + 2 < list.Length; t += 3)
                        {
                            if (list[t] < 0 || list[t + 1] < 0 || list[t + 2] < 0 || list[t] >= count || list[t + 1] >= count || list[t + 2] >= count)
                            {
                                continue;
                            }
                            int a = Group[list[t]], b = Group[list[t + 1]], c = Group[list[t + 2]];
                            AddEdge(edges, degree, a, b);
                            AddEdge(edges, degree, b, c);
                            AddEdge(edges, degree, c, a);
                            if (a != b && b != c && c != a)
                            {
                                groupTriangles.Add(a);
                                groupTriangles.Add(b);
                                groupTriangles.Add(c);
                            }
                        }
                    }
                }
                GroupTriangles = groupTriangles.ToArray();
                NeighbourStart = new int[GroupCount + 1];
                for (int g = 0; g < GroupCount; g++)
                {
                    NeighbourStart[g + 1] = NeighbourStart[g] + degree[g];
                }
                Neighbours = new int[NeighbourStart[GroupCount]];
                var cursor = new int[GroupCount];
                foreach (long edge in edges)
                {
                    int a = (int)(edge >> 32), b = (int)(edge & 0xFFFFFFFF);
                    Neighbours[NeighbourStart[a] + cursor[a]++] = b;
                    Neighbours[NeighbourStart[b] + cursor[b]++] = a;
                }
            }

            private static void AddEdge(HashSet<long> edges, int[] degree, int a, int b)
            {
                if (a == b)
                {
                    return;
                }
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (edges.Add(key))
                {
                    degree[a]++;
                    degree[b]++;
                }
            }

            /// <summary>The surface direction at each welded vertex: its triangles' normals, weighted by area.</summary>
            public Vector3[] SurfaceNormals(Vector3[] positions)
            {
                var sums = new Vector3[GroupCount];
                int count = positions.Length;
                if (triangles != null)
                {
                    for (int s = 0; s < triangles.Count; s++)
                    {
                        int[] list = triangles[s];
                        if (list == null)
                        {
                            continue;
                        }
                        for (int t = 0; t + 2 < list.Length; t += 3)
                        {
                            int i0 = list[t], i1 = list[t + 1], i2 = list[t + 2];
                            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= count || i1 >= count || i2 >= count)
                            {
                                continue;
                            }
                            Vector3 cross = Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
                            sums[Group[i0]] += cross;
                            sums[Group[i1]] += cross;
                            sums[Group[i2]] += cross;
                        }
                    }
                }
                for (int g = 0; g < GroupCount; g++)
                {
                    sums[g] = sums[g].sqrMagnitude > 1e-20f ? sums[g].normalized : Vector3.zero;
                }
                return sums;
            }
        }
    }
}
