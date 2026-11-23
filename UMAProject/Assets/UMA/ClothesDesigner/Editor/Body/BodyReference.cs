using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The body clothes are fitted to, captured once from the stage's character in the pose shown:
    /// its skin (for weights), its surface (for collision), which of it is arm, and its landmarks and
    /// measurements. Everything is in the character renderer's space, in metres.
    /// </summary>
    public sealed class BodyReference
    {
        public UMASlotSkinTransfer.TargetData Skin;
        public UMASlotSkinTransfer.TargetSurface Surface;
        public BodyCollider Collider;
        public BodyLandmarks Landmarks;
        /// <summary>Per surface vertex: mostly weighted to an arm (upper arm and below).</summary>
        public bool[] ArmVertex;
        public int SlotCount;
        public Bounds Bounds;
        public double CaptureMilliseconds;
        /// <summary>A fingerprint of the body's shape, to know when a fit was made on another body.</summary>
        public string Revision;
        /// <summary>Bone index (into Skin.Bones) of the chest, used for vertices no body surface is near.</summary>
        public int FallbackBone;

        /// <summary>Captures the character as it stands. The character is not generated again.</summary>
        public static BodyReference Capture(DynamicCharacterAvatar avatar)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var body = new BodyReference();
            body.Skin = UMASlotSkinTransfer.CaptureTargetData(avatar, 0, false);
            body.Surface = UMASlotSkinTransfer.BuildTargetSurface(body.Skin);
            body.Collider = new BodyCollider(body.Surface.Triangles);
            body.Bounds = body.Collider.BodyBounds;

            var slots = new HashSet<int>();
            foreach (UMASlotSkinTransfer.TriangleInfo t in body.Surface.Triangles) if (t.SlotIndex >= 0) slots.Add(t.SlotIndex);
            body.SlotCount = slots.Count;

            Animator animator = avatar.umaData != null ? avatar.umaData.animator : null;
            bool human = animator != null && animator.avatar != null && animator.avatar.isHuman;
            body.ArmVertex = FindArmVertices(body, human ? animator : null);
            body.FallbackBone = FindBone(body.Skin, human ? animator.GetBoneTransform(HumanBodyBones.Chest) : null);
            body.Landmarks = MeasureLandmarks(body, human ? animator : null);
            body.Revision = body.Surface.Triangles.Length + ":" + body.Bounds.size.ToString("F4") + ":" + body.Landmarks.chestCm.ToString("F1");
            timer.Stop();
            body.CaptureMilliseconds = timer.Elapsed.TotalMilliseconds;
            return body;
        }

        private static int FindBone(UMASlotSkinTransfer.TargetData skin, Transform bone)
        {
            if (bone != null)
            {
                for (int i = 0; i < skin.Bones.Length; i++) if (skin.Bones[i] == bone) return i;
            }
            return 0;
        }

        private static bool[] FindArmVertices(BodyReference body, Animator animator)
        {
            int count = body.Surface.VertexWeights.Length;
            var result = new bool[count];
            if (animator == null) return result;
            var armBones = new HashSet<int>();
            foreach (HumanBodyBones hb in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
            {
                Transform root = animator.GetBoneTransform(hb);
                if (root == null) continue;
                for (int i = 0; i < body.Skin.Bones.Length; i++)
                {
                    Transform b = body.Skin.Bones[i];
                    if (b != null && (b == root || b.IsChildOf(root))) armBones.Add(i);
                }
            }
            for (int v = 0; v < count; v++)
            {
                float w = 0f;
                List<BoneWeight1> weights = body.Surface.VertexWeights[v];
                if (weights == null) continue;
                foreach (BoneWeight1 bw in weights) if (armBones.Contains(bw.boneIndex)) w += bw.weight;
                result[v] = w > 0.5f;
            }
            return result;
        }

        private bool TriangleIsArm(UMASlotSkinTransfer.TriangleInfo t) => ArmVertex[t.Index0] && ArmVertex[t.Index1];

        private Vector3 LocalBonePosition(Animator animator, HumanBodyBones bone, out bool found)
        {
            Transform t = animator != null ? animator.GetBoneTransform(bone) : null;
            found = t != null;
            return found ? Skin.Renderer.transform.InverseTransformPoint(t.position) : Vector3.zero;
        }

        private static BodyLandmarks MeasureLandmarks(BodyReference body, Animator animator)
        {
            var marks = new BodyLandmarks();
            Bounds b = body.Bounds;
            marks.heightCm = b.size.y * 100f;
            Vector3 leftArm = body.LocalBonePosition(animator, HumanBodyBones.LeftUpperArm, out bool hasLeft);
            Vector3 rightArm = body.LocalBonePosition(animator, HumanBodyBones.RightUpperArm, out bool hasRight);
            Vector3 hips = body.LocalBonePosition(animator, HumanBodyBones.Hips, out bool hasHips);
            Vector3 neck = body.LocalBonePosition(animator, HumanBodyBones.Neck, out bool hasNeck);
            if (hasLeft && hasRight && hasHips)
            {
                float armY = (leftArm.y + rightArm.y) * 0.5f;
                float torso = armY - hips.y;
                marks.chestY = armY - 0.24f * torso;
                marks.waistY = hips.y + 0.42f * torso;
                marks.hipY = hips.y - 0.15f * torso;
                // The top of the shoulder: the highest torso point between the neck and the shoulder joint.
                float armX = Mathf.Max(Mathf.Abs(leftArm.x), Mathf.Abs(rightArm.x));
                float limit = hasNeck ? neck.y + 0.03f : armY + 0.12f;
                float top = armY;
                foreach (UMASlotSkinTransfer.TriangleInfo t in body.Surface.Triangles)
                {
                    if (body.TriangleIsArm(t)) continue;
                    Vector3 p = t.A;
                    float ax = Mathf.Abs(p.x);
                    if (ax > armX * 0.55f && ax < armX && p.y > top && p.y < limit) top = p.y;
                }
                marks.shoulderTopY = top;
            }
            else
            {
                marks.shoulderTopY = b.min.y + b.size.y * 0.84f;
                marks.chestY = b.min.y + b.size.y * 0.75f;
                marks.waistY = b.min.y + b.size.y * 0.65f;
                marks.hipY = b.min.y + b.size.y * 0.5f;
            }
            marks.chestCm = body.Circumference(marks.chestY, out Rect chestBox) * 100f;
            marks.waistCm = body.Circumference(marks.waistY, out _) * 100f;
            marks.hipCm = body.Circumference(marks.hipY, out _) * 100f;
            marks.torsoCenter = chestBox.center;
            marks.valid = marks.chestCm > 0f;
            if (marks.valid) MeasureLimbs(body, animator, marks);
            return marks;
        }

        /// <summary>
        /// The arms, legs and head: joints from the skeleton, the crotch from the body, how far the limb
        /// surfaces reach from their axes, and the measurements patterns can follow.
        /// </summary>
        private static void MeasureLimbs(BodyReference body, Animator animator, BodyLandmarks marks)
        {
            Bounds b = body.Bounds;
            marks.headTopY = b.max.y;
            marks.leftShoulder = body.LocalBonePosition(animator, HumanBodyBones.LeftUpperArm, out bool a1);
            marks.rightShoulder = body.LocalBonePosition(animator, HumanBodyBones.RightUpperArm, out bool a2);
            marks.leftWrist = body.LocalBonePosition(animator, HumanBodyBones.LeftHand, out bool a3);
            marks.rightWrist = body.LocalBonePosition(animator, HumanBodyBones.RightHand, out bool a4);
            marks.leftHip = body.LocalBonePosition(animator, HumanBodyBones.LeftUpperLeg, out bool l1);
            marks.rightHip = body.LocalBonePosition(animator, HumanBodyBones.RightUpperLeg, out bool l2);
            marks.leftAnkle = body.LocalBonePosition(animator, HumanBodyBones.LeftFoot, out bool l3);
            marks.rightAnkle = body.LocalBonePosition(animator, HumanBodyBones.RightFoot, out bool l4);
            marks.head = body.LocalBonePosition(animator, HumanBodyBones.Head, out bool h1);
            Vector3 neck = body.LocalBonePosition(animator, HumanBodyBones.Neck, out bool h2);
            marks.limbs = a1 && a2 && a3 && a4 && l1 && l2 && l3 && l4 && h1 && h2;
            if (!marks.limbs) return;
            marks.neckY = neck.y;
            float cx = marks.torsoCenter.x;

            // The crotch: going down from the hip line, where nothing of the body is left on the midline.
            float ankleY = (marks.leftAnkle.y + marks.rightAnkle.y) * 0.5f;
            marks.crotchY = marks.hipY;
            for (float y = marks.hipY; y > ankleY; y -= 0.005f)
            {
                bool onMidline = false;
                foreach (Vector2 p in body.CrossSection(y)) if (Mathf.Abs(p.x - cx) < 0.015f) { onMidline = true; break; }
                marks.crotchY = y;
                if (!onMidline) break;
            }

            // Arms: distances of arm surface points from the shoulder-to-wrist axis.
            var armDistances = new List<float>();
            var upperArm = new List<float>();
            foreach (bool left in new[] { true, false })
            {
                Vector3 root = left ? marks.leftShoulder : marks.rightShoulder;
                Vector3 axis = (left ? marks.leftWrist : marks.rightWrist) - root;
                float length = axis.magnitude;
                if (length < 1e-4f) continue;
                axis /= length;
                foreach (UMASlotSkinTransfer.TriangleInfo t in body.Surface.Triangles)
                {
                    if (!body.TriangleIsArm(t) || (t.A.x < cx) != left) continue;
                    Vector3 d = t.A - root;
                    float s = Vector3.Dot(d, axis) / length;
                    if (s < 0.1f || s > 0.95f) continue;
                    float r = (d - axis * (s * length)).magnitude;
                    armDistances.Add(r);
                    if (s > 0.2f && s < 0.3f) upperArm.Add(r);
                }
            }
            marks.armRadius = Percentile(armDistances, 0.9f, 0.05f);
            marks.upperArmCm = 2f * Mathf.PI * Percentile(upperArm, 0.5f, 0.045f) * 100f;
            marks.armLengthCm = ((marks.leftWrist - marks.leftShoulder).magnitude + (marks.rightWrist - marks.rightShoulder).magnitude) * 50f;

            // Legs: horizontal distances of leg surface points from the hip-to-ankle line at their height.
            var legDistances = new List<float>();
            foreach (UMASlotSkinTransfer.TriangleInfo t in body.Surface.Triangles)
            {
                if (body.TriangleIsArm(t)) continue;
                Vector3 p = t.A;
                if (p.y > marks.crotchY - 0.03f || p.y < ankleY + 0.05f) continue;
                bool left = p.x < cx;
                Vector3 c = marks.LegAxisAt(left, p.y);
                legDistances.Add(new Vector2(p.x - c.x, p.z - c.z).magnitude);
            }
            marks.legRadius = Percentile(legDistances, 0.9f, 0.08f);
            marks.waistToAnkleCm = (marks.waistY - ankleY) * 100f;
            marks.inseamCm = (marks.crotchY - ankleY) * 100f;
            var leftThigh = new List<Vector2>();
            var rightThigh = new List<Vector2>();
            foreach (Vector2 p in body.CrossSection(marks.crotchY - 0.04f)) (p.x < cx ? leftThigh : rightThigh).Add(p);
            marks.thighCm = (HullPerimeter(leftThigh) + HullPerimeter(rightThigh)) * 50f;

            // Head and neck.
            float neckMid = (neck.y + marks.head.y) * 0.5f;
            marks.neckCm = body.Circumference(neckMid, out _) * 100f;
            float headMax = 0f;
            Rect headBox = new Rect(marks.head.x - 0.08f, marks.head.z - 0.1f, 0.16f, 0.2f);
            for (float y = marks.head.y + 0.03f; y < marks.headTopY - 0.03f; y += 0.01f)
            {
                float c = body.Circumference(y, out Rect box) * 100f;
                if (c > headMax) { headMax = c; headBox = box; }
            }
            marks.headCm = headMax;
            marks.headCenter = headBox.center;
            marks.headHalf = body.HalfExtentsAbout(marks.headCenter, neckMid, marks.headTopY - 0.01f);
        }

        private static float Percentile(List<float> values, float fraction, float fallback)
        {
            if (values.Count == 0) return fallback;
            values.Sort();
            return values[Mathf.Clamp(Mathf.RoundToInt((values.Count - 1) * fraction), 0, values.Count - 1)];
        }

        private static float HullPerimeter(List<Vector2> points)
        {
            if (points.Count < 3) return 0f;
            List<Vector2> hull = ConvexHull(points);
            float perimeter = 0f;
            for (int i = 0; i < hull.Count; i++) perimeter += (hull[i] - hull[(i + 1) % hull.Count]).magnitude;
            return perimeter;
        }

        /// <summary>
        /// The tape-measure length around the body (arms left out) at a height: the perimeter of the
        /// convex outline of the cross-section, in metres. <paramref name="box"/> is the section's x/z extent.
        /// </summary>
        public float Circumference(float y, out Rect box)
        {
            List<Vector2> points = CrossSection(y);
            box = new Rect();
            if (points.Count < 3) return 0f;
            Vector2 min = points[0], max = points[0];
            foreach (Vector2 p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            box = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            List<Vector2> hull = ConvexHull(points);
            float perimeter = 0f;
            for (int i = 0; i < hull.Count; i++) perimeter += (hull[i] - hull[(i + 1) % hull.Count]).magnitude;
            return perimeter;
        }

        /// <summary>Where the body (arms left out) crosses a horizontal plane, as (x, z) points.</summary>
        public List<Vector2> CrossSection(float y)
        {
            var points = new List<Vector2>();
            foreach (UMASlotSkinTransfer.TriangleInfo t in Surface.Triangles)
            {
                if (TriangleIsArm(t)) continue;
                AddCrossing(points, t.A, t.B, y);
                AddCrossing(points, t.B, t.C, y);
                AddCrossing(points, t.C, t.A, y);
            }
            return points;
        }

        private static void AddCrossing(List<Vector2> points, Vector3 p, Vector3 q, float y)
        {
            if ((p.y - y) * (q.y - y) >= 0f) return;
            float s = (y - p.y) / (q.y - p.y);
            Vector3 x = Vector3.Lerp(p, q, s);
            points.Add(new Vector2(x.x, x.z));
        }

        public static List<Vector2> ConvexHull(List<Vector2> input)
        {
            var pts = new List<Vector2>(input);
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    Vector2 p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>
        /// The largest half-width and half-depth of the body (arms left out) between two heights, about
        /// the torso's centre, for laying pieces round it.
        /// </summary>
        public Vector2 HalfExtents(float y0, float y1) => HalfExtentsAbout(Landmarks.torsoCenter, y0, y1);

        /// <summary>The same about another centre (x, z), for the head.</summary>
        public Vector2 HalfExtentsAbout(Vector2 c, float y0, float y1)
        {
            float halfX = 0f, halfZ = 0f;
            for (float y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y += 0.02f)
            {
                foreach (Vector2 p in CrossSection(y))
                {
                    halfX = Mathf.Max(halfX, Mathf.Abs(p.x - c.x));
                    halfZ = Mathf.Max(halfZ, Mathf.Abs(p.y - c.y));
                }
            }
            return new Vector2(halfX, halfZ);
        }

        /// <summary>A copy of the surface without arm triangles, for weight donors on clothes with no sleeves.</summary>
        public UMASlotSkinTransfer.TargetSurface SurfaceWithoutArms()
        {
            var kept = new List<UMASlotSkinTransfer.TriangleInfo>();
            foreach (UMASlotSkinTransfer.TriangleInfo t in Surface.Triangles) if (!TriangleIsArm(t)) kept.Add(t);
            return new UMASlotSkinTransfer.TargetSurface
            {
                Triangles = kept.ToArray(),
                VertexWeights = Surface.VertexWeights,
                CurrentSkinMatrices = Surface.CurrentSkinMatrices,
                SampledRendererCount = Surface.SampledRendererCount
            };
        }
    }

    /// <summary>The body's triangles in a uniform grid, for cloth collision and closest-point queries.</summary>
    public sealed class BodyCollider
    {
        public readonly Vector3[] A, B, C, N;
        public readonly Vector3 GridOrigin;
        public readonly float CellSize;
        public readonly Vector3Int Dims;
        /// <summary>Start of each cell's triangle list in <see cref="CellTriangles"/>; Length = cells + 1.</summary>
        public readonly int[] CellStart;
        public readonly int[] CellTriangles;
        public readonly Bounds BodyBounds;

        public int TriangleCount => A.Length;

        public BodyCollider(UMASlotSkinTransfer.TriangleInfo[] triangles, float cellSize = 0.03f)
        {
            int n = triangles.Length;
            A = new Vector3[n]; B = new Vector3[n]; C = new Vector3[n]; N = new Vector3[n];
            Bounds bounds = new Bounds(n > 0 ? triangles[0].A : Vector3.zero, Vector3.zero);
            for (int i = 0; i < n; i++)
            {
                A[i] = triangles[i].A; B[i] = triangles[i].B; C[i] = triangles[i].C;
                Vector3 normal = Vector3.Cross(B[i] - A[i], C[i] - A[i]);
                N[i] = normal.sqrMagnitude > 1e-20f ? normal.normalized : Vector3.up;
                bounds.Encapsulate(A[i]); bounds.Encapsulate(B[i]); bounds.Encapsulate(C[i]);
            }
            BodyBounds = bounds;
            CellSize = cellSize;
            bounds.Expand(0.4f);
            GridOrigin = bounds.min;
            Dims = new Vector3Int(
                Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize)));
            int cells = Dims.x * Dims.y * Dims.z;
            var counts = new int[cells + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                int[] fill = pass == 1 ? new int[cells] : null;
                for (int i = 0; i < n; i++)
                {
                    Vector3 min = Vector3.Min(A[i], Vector3.Min(B[i], C[i]));
                    Vector3 max = Vector3.Max(A[i], Vector3.Max(B[i], C[i]));
                    Vector3Int c0 = Cell(min), c1 = Cell(max);
                    for (int z = c0.z; z <= c1.z; z++)
                        for (int y = c0.y; y <= c1.y; y++)
                            for (int x = c0.x; x <= c1.x; x++)
                            {
                                int cell = x + Dims.x * (y + Dims.y * z);
                                if (pass == 0) counts[cell + 1]++;
                                else CellTriangles[CellStart[cell] + fill[cell]++] = i;
                            }
                }
                if (pass == 0)
                {
                    for (int c = 0; c < cells; c++) counts[c + 1] += counts[c];
                    CellStart = counts;
                    CellTriangles = new int[counts[cells]];
                }
            }
        }

        public Vector3Int Cell(Vector3 p)
        {
            Vector3 q = (p - GridOrigin) / CellSize;
            return new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(q.x), 0, Dims.x - 1),
                Mathf.Clamp(Mathf.FloorToInt(q.y), 0, Dims.y - 1),
                Mathf.Clamp(Mathf.FloorToInt(q.z), 0, Dims.z - 1));
        }

        /// <summary>
        /// The closest body point within one cell of <paramref name="p"/>: its triangle, the point, and the
        /// signed distance along the triangle's outward normal. False when no triangle is that near.
        /// </summary>
        public bool Closest(Vector3 p, out int triangle, out Vector3 point, out float signedDistance) => Closest(p, 0f, out triangle, out point, out signedDistance);

        /// <summary>The nearest body triangle, searching at least <paramref name="radius"/> metres round the point. False when there is none that close.</summary>
        public bool Closest(Vector3 p, float radius, out int triangle, out Vector3 point, out float signedDistance)
        {
            Vector3Int c = Cell(p);
            int r = Mathf.Max(1, Mathf.CeilToInt(radius / Mathf.Max(1e-4f, CellSize)));
            Vector3Int lo = new Vector3Int(Mathf.Max(0, c.x - r), Mathf.Max(0, c.y - r), Mathf.Max(0, c.z - r));
            Vector3Int hi = new Vector3Int(Mathf.Min(Dims.x - 1, c.x + r), Mathf.Min(Dims.y - 1, c.y + r), Mathf.Min(Dims.z - 1, c.z + r));
            return Search(p, lo, hi, out triangle, out point, out signedDistance, out _);
        }

        /// <summary>
        /// The same nearest triangle as <see cref="Closest(Vector3, out int, out Vector3, out float)"/>, searching
        /// only the cells within <paramref name="near"/> metres first. A hit that close is exact (every nearer
        /// triangle touches those cells), so only points farther from the body pay for the full search.
        /// Thread safe: the collider is read only.
        /// </summary>
        public bool ClosestNear(Vector3 p, float near, out int triangle, out Vector3 point, out float signedDistance)
        {
            Vector3 h = new Vector3(near, near, near);
            if (near > 0f && near < CellSize
                && Search(p, Cell(p - h), Cell(p + h), out triangle, out point, out signedDistance, out float best)
                && best <= near * near)
            {
                return true;
            }
            return Closest(p, 0f, out triangle, out point, out signedDistance);
        }

        private bool Search(Vector3 p, Vector3Int lo, Vector3Int hi, out int triangle, out Vector3 point, out float signedDistance, out float best)
        {
            triangle = -1; point = p; signedDistance = float.MaxValue;
            best = float.MaxValue;
            float bestAlong = 0f;
            for (int z = lo.z; z <= hi.z; z++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int x = lo.x; x <= hi.x; x++)
                    {
                        int cell = x + Dims.x * (y + Dims.y * z);
                        for (int k = CellStart[cell]; k < CellStart[cell + 1]; k++)
                        {
                            int t = CellTriangles[k];
                            // A triangle whose plane is already farther than the best cannot be nearer.
                            float plane = Vector3.Dot(p - A[t], N[t]);
                            float tie = best * 1e-4f + 1e-12f;
                            if (plane * plane > best + tie) continue;
                            Vector3 q = UMASlotSkinTransfer.ClosestPointOnTriangle(p, A[t], B[t], C[t]);
                            float d = (q - p).sqrMagnitude;
                            if (d > best + tie) continue;
                            // Nearest points on a shared edge or corner tie, and the triangles there can
                            // disagree on the side. Keep the one whose normal lines up best with the offset,
                            // so inside or outside does not depend on the order the cells are searched.
                            float along = Mathf.Abs(Vector3.Dot(p - q, N[t]));
                            if (d < best - tie || along > bestAlong)
                            {
                                best = Mathf.Min(best, d); bestAlong = along; triangle = t; point = q;
                            }
                        }
                    }
            if (triangle < 0) return false;
            signedDistance = Vector3.Dot(p - point, N[triangle]);
            return true;
        }
    }
}
