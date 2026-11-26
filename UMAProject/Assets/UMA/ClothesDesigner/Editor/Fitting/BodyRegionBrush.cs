using System.Collections.Generic;
using UMA.Editors;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The Body Region tool's view of the body: its triangles welded by position (so slots that meet are one
    /// surface), which triangles touch, and the part each lies on. Painting fills outwards across touching triangles
    /// from the one under the brush, so eyes, lashes and the inside of the mouth (separate meshes) are never picked up.
    /// <see cref="Patches"/> turns a painted region into surface patches, one per part and connected stretch, out from
    /// the body by the clearance, for <see cref="RegionUnfold"/>.
    /// </summary>
    public sealed class BodyRegionBrush
    {
        public readonly BodyReference Body;
        /// <summary>The welded vertex of each triangle corner (three per triangle).</summary>
        private readonly int[] corner;
        private readonly Vector3[] welded;
        private readonly int[][] touching;
        private readonly BodyPart[] partOf;
        private readonly Vector3[] centre;

        public int TriangleCount => partOf.Length;

        public BodyRegionBrush(BodyReference body)
        {
            Body = body;
            BodyCollider c = body.Collider;
            int n = c.TriangleCount;
            corner = new int[n * 3];
            centre = new Vector3[n];
            var ids = new Dictionary<Vector3Int, int>(n * 2);
            var positions = new List<Vector3>(n * 2);
            int Weld(Vector3 p)
            {
                var cell = new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                if (!ids.TryGetValue(cell, out int id)) { id = positions.Count; ids[cell] = id; positions.Add(p); }
                return id;
            }
            for (int t = 0; t < n; t++)
            {
                corner[3 * t] = Weld(c.A[t]); corner[3 * t + 1] = Weld(c.B[t]); corner[3 * t + 2] = Weld(c.C[t]);
                centre[t] = (c.A[t] + c.B[t] + c.C[t]) / 3f;
            }
            welded = positions.ToArray();
            var byEdge = new Dictionary<long, List<int>>(n * 3);
            for (int t = 0; t < n; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = corner[3 * t + k], b = corner[3 * t + (k + 1) % 3];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!byEdge.TryGetValue(key, out List<int> list)) byEdge[key] = list = new List<int>(2);
                    list.Add(t);
                }
            }
            var near = new List<int>[n];
            for (int t = 0; t < n; t++) near[t] = new List<int>(3);
            foreach (List<int> list in byEdge.Values)
            {
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++) { near[list[i]].Add(list[j]); near[list[j]].Add(list[i]); }
            }
            touching = new int[n][];
            for (int t = 0; t < n; t++) touching[t] = near[t].ToArray();
            partOf = new BodyPart[n];
            for (int t = 0; t < n; t++) partOf[t] = PartAt(t);
        }

        /// <summary>The middle of the body side to side: halfway between the shoulders.</summary>
        private float MiddleX => (Body.Landmarks.leftShoulder.x + Body.Landmarks.rightShoulder.x) * 0.5f;

        /// <summary>The direction of the body's left (angle 90 round the torso, head and legs).</summary>
        public Vector3 LeftDirection => Body.Landmarks.leftShoulder.x < MiddleX ? Vector3.left : Vector3.right;

        public BodyPart PartOf(int triangle) => partOf[triangle];

        /// <summary>A triangle's part: the head above the neck; an arm where its corners are mostly arm (by bone weight); a leg below the crotch; else the torso.</summary>
        private BodyPart PartAt(int t)
        {
            BodyLandmarks m = Body.Landmarks;
            Vector3 p = centre[t];
            if (p.y > m.neckY + 0.02f) return BodyPart.Head;
            bool left = (p.x < MiddleX) == (m.leftShoulder.x < MiddleX);
            bool[] arm = Body.ArmVertex;
            UMASlotSkinTransfer.TriangleInfo info = Body.Surface.Triangles[t];
            if (arm != null && info.Index0 < arm.Length && info.Index1 < arm.Length && info.Index2 < arm.Length)
            {
                int votes = (arm[info.Index0] ? 1 : 0) + (arm[info.Index1] ? 1 : 0) + (arm[info.Index2] ? 1 : 0);
                if (votes >= 2) return left ? BodyPart.LeftArm : BodyPart.RightArm;
            }
            else if (m.limbs)
            {
                // No arm weights: beyond the shoulder joint and near the arm's line.
                Vector3 s = left ? m.leftShoulder : m.rightShoulder, w = left ? m.leftWrist : m.rightWrist;
                Vector3 along = w - s;
                float u = Vector3.Dot(p - s, along) / Mathf.Max(1e-6f, along.sqrMagnitude);
                if (u > 0.05f && Vector3.Distance(p, s + along * Mathf.Min(u, 1f)) < Mathf.Max(0.05f, m.armRadius * 2.5f)) return left ? BodyPart.LeftArm : BodyPart.RightArm;
            }
            if (p.y < m.crotchY - 0.01f) return left ? BodyPart.LeftLeg : BodyPart.RightLeg;
            return BodyPart.Torso;
        }

        /// <summary>The point's mirror image across the body's middle.</summary>
        public Vector3 Mirror(Vector3 p) => new Vector3(2f * MiddleX - p.x, p.y, p.z);

        /// <summary>The triangle nearest a point on (or near) the body, or -1.</summary>
        public int TriangleNear(Vector3 p)
        {
            return Body.Collider.ClosestNear(p, 0.05f, out int t, out _, out _) ? t : -1;
        }

        /// <summary>
        /// Adds (or with <paramref name="erase"/> removes) the triangles within <paramref name="radius"/> of a point,
        /// filling outwards across touching triangles from the one under it. Returns how many changed.
        /// </summary>
        public int Paint(Vector3 point, float radius, ISet<int> region, bool erase)
        {
            int start = TriangleNear(point);
            if (start < 0) return 0;
            int changed = 0;
            float r2 = radius * radius;
            var seen = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                if ((centre[t] - point).sqrMagnitude > r2 && t != start) continue;
                if (erase ? region.Remove(t) : region.Add(t)) changed++;
                foreach (int o in touching[t]) if (seen.Add(o)) queue.Enqueue(o);
            }
            return changed;
        }

        /// <summary>The triangle's corners on the body (for drawing the region).</summary>
        public void Corners(int t, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = Body.Collider.A[t]; b = Body.Collider.B[t]; c = Body.Collider.C[t];
        }

        /// <summary>
        /// The painted region as surface patches: grouped by part, each connected stretch on its own, every point moved
        /// out from the body by <paramref name="outward"/> metres, with the part's axis and the side its cut is put on.
        /// </summary>
        /// <summary>How many painted triangles the last <see cref="Patches"/> left out (small scraps on their own).</summary>
        public int LeftOut { get; private set; }

        public List<RegionPatch> Patches(ICollection<int> region, float outward)
        {
            LeftOut = 0;
            var patches = new List<RegionPatch>();
            var inRegion = new HashSet<int>(region);
            Tidy(inRegion);
            // Each triangle's part, with small scraps of one part (a few armpit triangles in an arm band) given to
            // the part round them.
            var part = new Dictionary<int, BodyPart>();
            foreach (int t in inRegion) part[t] = partOf[t];
            AbsorbScraps(inRegion, part);
            var done = new HashSet<int>();
            var counts = new Dictionary<BodyPart, int>();
            foreach (int seed in new List<int>(inRegion))
            {
                if (done.Contains(seed)) continue;
                BodyPart seedPart = part[seed];
                // The connected stretch of this part's painted triangles.
                var stretch = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                done.Add(seed);
                while (queue.Count > 0)
                {
                    int t = queue.Dequeue();
                    stretch.Add(t);
                    foreach (int o in touching[t])
                    {
                        if (!inRegion.Contains(o) || part[o] != seedPart || !done.Add(o)) continue;
                        queue.Enqueue(o);
                    }
                }
                if (stretch.Count < 6) continue;
                counts.TryGetValue(seedPart, out int count);
                counts[seedPart] = count + 1;
                RegionPatch patch = Frame(seedPart);
                patch.Name = PartName(seedPart) + (count > 0 ? " " + (count + 1) : "");
                var local = new Dictionary<int, int>();
                var normals = new List<Vector3>();
                foreach (int t in stretch)
                {
                    Vector3 face = Body.Collider.N[t];
                    for (int k = 0; k < 3; k++)
                    {
                        int id = corner[3 * t + k];
                        if (!local.TryGetValue(id, out int v))
                        {
                            v = patch.Positions.Count;
                            local[id] = v;
                            patch.Positions.Add(welded[id]);
                            patch.Ids.Add(id);
                            normals.Add(Vector3.zero);
                        }
                        normals[v] += face;
                        patch.Triangles.Add(v);
                    }
                }
                for (int v = 0; v < patch.Positions.Count; v++) patch.Positions[v] += normals[v].normalized * outward;
                patches.Add(patch);
            }
            return patches;
        }

        /// <summary>
        /// Tidies a painted region: fills notches and pinholes (unpainted triangles with two or three painted
        /// neighbours) and drops loose triangles (no painted neighbour across an edge), so its edge is clean.
        /// </summary>
        private void Tidy(HashSet<int> region)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var add = new List<int>();
                foreach (int t in region)
                {
                    foreach (int o in touching[t])
                    {
                        if (region.Contains(o)) continue;
                        int painted = 0;
                        foreach (int p in touching[o]) if (region.Contains(p)) painted++;
                        if (painted >= 2) add.Add(o);
                    }
                }
                foreach (int t in add) region.Add(t);
                if (add.Count == 0) break;
            }
            var loose = new List<int>();
            foreach (int t in region)
            {
                int painted = 0;
                foreach (int o in touching[t]) if (region.Contains(o)) painted++;
                if (painted == 0) loose.Add(t);
            }
            foreach (int t in loose) region.Remove(t);
        }

        /// <summary>Gives each small connected scrap of one part (under 60 triangles) to the part most of its painted neighbours are.</summary>
        private void AbsorbScraps(HashSet<int> region, Dictionary<int, BodyPart> part)
        {
            var seen = new HashSet<int>();
            foreach (int seed in new List<int>(region))
            {
                if (!seen.Add(seed)) continue;
                var scrap = new List<int> { seed };
                var stack = new Stack<int>();
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    foreach (int o in touching[t])
                    {
                        if (!region.Contains(o) || part[o] != part[seed] || !seen.Add(o)) continue;
                        scrap.Add(o);
                        stack.Push(o);
                    }
                }
                if (scrap.Count >= 60) continue;
                var votes = new Dictionary<BodyPart, int>();
                foreach (int t in scrap)
                    foreach (int o in touching[t])
                        if (region.Contains(o) && part[o] != part[seed]) { votes.TryGetValue(part[o], out int c); votes[part[o]] = c + 1; }
                if (votes.Count == 0)
                {
                    // A scrap on its own (a dab that caught the side of the chest): left out.
                    foreach (int t in scrap) region.Remove(t);
                    LeftOut += scrap.Count;
                    continue;
                }
                BodyPart into = part[seed];
                int most = 0;
                foreach (KeyValuePair<BodyPart, int> kv in votes) if (kv.Value > most) { most = kv.Value; into = kv.Key; }
                foreach (int t in scrap) part[t] = into;
            }
        }

        private static string PartName(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.LeftArm: return "Left Arm";
                case BodyPart.RightArm: return "Right Arm";
                case BodyPart.LeftLeg: return "Left Leg";
                case BodyPart.RightLeg: return "Right Leg";
                default: return part.ToString();
            }
        }

        /// <summary>A part's axis and directions: up the torso, head and legs, towards the shoulder on an arm; cut at the back, under the arm, inside the leg.</summary>
        private RegionPatch Frame(BodyPart part)
        {
            BodyLandmarks m = Body.Landmarks;
            var patch = new RegionPatch { Part = part };
            Vector3 left = LeftDirection;
            float middle = MiddleX;
            switch (part)
            {
                case BodyPart.Head:
                    patch.AxisPoint = new Vector3(m.head.x, 0f, m.head.z);
                    patch.Up = Vector3.up; patch.CutSide = Vector3.back; patch.Front = Vector3.forward; patch.Ninety = left;
                    break;
                case BodyPart.LeftArm:
                case BodyPart.RightArm:
                {
                    bool isLeft = part == BodyPart.LeftArm;
                    Vector3 s = isLeft ? m.leftShoulder : m.rightShoulder, w = isLeft ? m.leftWrist : m.rightWrist;
                    Vector3 up = (s - w).sqrMagnitude > 1e-8f ? (s - w).normalized : (isLeft ? -left : left);
                    patch.AxisPoint = s; patch.Up = up;
                    patch.Front = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
                    patch.Ninety = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                    patch.CutSide = -patch.Front;
                    break;
                }
                case BodyPart.LeftLeg:
                case BodyPart.RightLeg:
                {
                    bool isLeft = part == BodyPart.LeftLeg;
                    Vector3 h = isLeft ? m.leftHip : m.rightHip, a = isLeft ? m.leftAnkle : m.rightAnkle;
                    patch.AxisPoint = h;
                    patch.Up = (h - a).sqrMagnitude > 1e-8f ? (h - a).normalized : Vector3.up;
                    patch.Front = Vector3.forward; patch.Ninety = left;
                    // Inside the leg: towards the body's middle.
                    patch.CutSide = new Vector3(middle - h.x, 0f, 0f).normalized;
                    if (patch.CutSide.sqrMagnitude < 0.5f) patch.CutSide = isLeft ? -left : left;
                    break;
                }
                default:
                    patch.AxisPoint = new Vector3(middle, 0f, m.torsoCenter.y);
                    patch.Up = Vector3.up; patch.CutSide = Vector3.back; patch.Front = Vector3.forward; patch.Ninety = left;
                    break;
            }
            return patch;
        }
    }
}
