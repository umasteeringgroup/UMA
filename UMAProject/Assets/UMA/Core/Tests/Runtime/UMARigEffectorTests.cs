using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// Profile effectors on hand-made geometry: how strength falls off inside a sphere and a capsule,
    /// what each action does, mirroring, how normals follow, and "clear the body" against a flat skin.
    /// </summary>
    public sealed class UMARigEffectorTests
    {
        private const float Tolerance = 1e-5f;

        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        /// <summary>A rig with a Hips joint one metre up, to place effectors from.</summary>
        private static UMARigRestPose Rig()
        {
            int global = UMAUtils.StringToHash("Global");
            var bones = new List<UMATransform>
            {
                new UMATransform { name = "Hips", hash = UMAUtils.StringToHash("Hips"), parent = global, position = new Vector3(0f, 1f, 0f), rotation = Quaternion.identity, scale = Vector3.one },
            };
            return UMARigRestPose.FromBones(bones, "Global", Matrix4x4.identity);
        }

        private static readonly Vector3 Hips = new Vector3(0f, 1f, 0f);

        private static UMARigEffector Sphere(float radius, float falloff)
        {
            return new UMARigEffector { boneName = "Hips", shape = UMARigEffectorShape.Sphere, radius = radius, falloff = falloff };
        }

        private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance, string what)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThanOrEqualTo(tolerance), $"{what}: expected {expected:F5}, was {actual:F5}");
        }

        /// <summary>A flat square sheet of (n + 1) by (n + 1) vertices in the xz plane, facing up.</summary>
        private static void Sheet(int n, float size, float height, out Vector3[] positions, out Vector3[] normals, out int[] triangles)
        {
            positions = new Vector3[(n + 1) * (n + 1)];
            normals = new Vector3[positions.Length];
            var list = new List<int>();
            for (int z = 0; z <= n; z++)
            {
                for (int x = 0; x <= n; x++)
                {
                    int i = z * (n + 1) + x;
                    positions[i] = new Vector3((x / (float)n - 0.5f) * size, height, (z / (float)n - 0.5f) * size);
                    normals[i] = Vector3.up;
                    if (x < n && z < n)
                    {
                        // Clockwise seen from above, which is a face pointing up in Unity.
                        list.Add(i); list.Add(i + n + 1); list.Add(i + 1);
                        list.Add(i + 1); list.Add(i + n + 1); list.Add(i + n + 2);
                    }
                }
            }
            triangles = list.ToArray();
        }

        // --- where an effector acts, and how strongly --------------------------------------------

        [Test]
        public void ASphereIsFullStrengthInItsCoreAndFadesToItsEdge()
        {
            UMARigRestPose rig = Rig();
            UMARigEffector effector = Sphere(0.1f, 0.5f);   // core out to 5 cm, fading to nothing at 10 cm
            Assert.AreEqual(1f, effector.GetWeight(Hips, rig), Tolerance);
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0.05f, 0f, 0f), rig), Tolerance);
            Assert.AreEqual(0.5f, effector.GetWeight(Hips + new Vector3(0f, 0.075f, 0f), rig), 1e-4f, "half way through the fade");
            Assert.AreEqual(0f, effector.GetWeight(Hips + new Vector3(0f, 0f, 0.1f), rig), Tolerance);
            Assert.AreEqual(0f, effector.GetWeight(Hips + new Vector3(0.3f, 0f, 0f), rig), Tolerance);
            float last = 1f;
            for (float d = 0f; d <= 0.11f; d += 0.005f)
            {
                float weight = effector.GetWeight(Hips + new Vector3(d, 0f, 0f), rig);
                Assert.That(weight, Is.LessThanOrEqualTo(last + Tolerance), "strength never rises with distance");
                last = weight;
            }

            effector.falloff = 0f;
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0.099f, 0f, 0f), rig), Tolerance, "no falloff is full strength to a hard edge");

            // The effector follows its bone and its own offset.
            effector.position = new Vector3(0f, 0f, -0.2f);
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0f, 0f, -0.2f), rig), Tolerance);
            Assert.AreEqual(0f, effector.GetWeight(Hips, rig), Tolerance);
            effector.GetFrame(rig, true, out Vector3 mirroredCentre, out _);
            AssertClose(new Vector3(0f, 1f, -0.2f), mirroredCentre, Tolerance, "an effector on the centre line mirrors onto itself");
        }

        [Test]
        public void ACapsuleMeasuresFromItsCentreLineAndFollowsItsCurveAlongTheLength()
        {
            UMARigRestPose rig = Rig();
            var effector = new UMARigEffector { boneName = "Hips", shape = UMARigEffectorShape.Capsule, radius = 0.1f, falloff = 0f, length = 0.4f };

            // Straight up by default: the centre line runs 20 cm above and below the Hips.
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0.05f, 0.15f, 0f), rig), Tolerance, "beside the centre line");
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0f, 0.25f, 0f), rig), Tolerance, "inside the rounded end");
            Assert.AreEqual(0f, effector.GetWeight(Hips + new Vector3(0f, 0.31f, 0f), rig), Tolerance, "past the rounded end");
            Assert.AreEqual(0f, effector.GetWeight(Hips + new Vector3(0.11f, 0f, 0f), rig), Tolerance, "further from the centre line than the radius");

            // The curve runs from the lower end (0) to the upper end (1).
            effector.axisCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.AreEqual(0f, effector.GetWeight(Hips + new Vector3(0.02f, -0.2f, 0f), rig), Tolerance, "lower end");
            Assert.AreEqual(0.5f, effector.GetWeight(Hips + new Vector3(0.02f, 0f, 0f), rig), 1e-4f, "middle");
            Assert.AreEqual(0.75f, effector.GetWeight(Hips + new Vector3(0.02f, 0.1f, 0f), rig), 1e-4f, "three quarters up");
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0.02f, 0.2f, 0f), rig), Tolerance, "upper end");
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0f, 0.26f, 0f), rig), Tolerance, "the rounded end takes the end's value");

            // A bump: nothing at either end, full in the middle.
            effector.axisCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            Assert.AreEqual(0f, effector.GetWeight(Hips + new Vector3(0.02f, 0.2f, 0f), rig), Tolerance);
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0.02f, 0f, 0f), rig), 1e-4f);
            Assert.That(effector.GetWeight(Hips + new Vector3(0.02f, 0.1f, 0f), rig), Is.InRange(0.2f, 0.8f));

            // Turned a quarter turn about z, the capsule lies along x; its upper end is toward -x.
            effector.axisCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            effector.rotationEuler = new Vector3(0f, 0f, 90f);
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(-0.2f, 0.05f, 0f), rig), 1e-4f, "upper end, now at -x");
            Assert.AreEqual(0f, effector.GetWeight(Hips + new Vector3(0f, 0.15f, 0f), rig), Tolerance, "no longer reaches up");
            Assert.AreEqual(1f, effector.GetWeight(Hips + new Vector3(0.2f, 0.05f, 0f), rig, mirrored: true), 1e-4f, "the mirror image has its upper end at +x");
        }

        // --- what the actions do -------------------------------------------------------------------

        [Test]
        public void PushMoveAndScaleActInsideTheRegionOnly()
        {
            UMARigRestPose rig = Rig();
            Vector3[] start =
            {
                Hips + new Vector3(0.02f, 0f, 0f),      // core
                Hips + new Vector3(0f, 0f, -0.075f),    // half strength
                Hips + new Vector3(0f, 0.5f, 0f),       // outside
            };

            UMARigEffector push = Sphere(0.1f, 0.5f);
            push.action = UMARigEffectorAction.Push;
            push.amount = 0.02f;
            var p = (Vector3[])start.Clone();
            var report = new UMARigEffectorReport();
            Assert.IsTrue(UMARigEffectorDeformer.Apply(new[] { push }, rig, null, p, null, null, null, report));
            AssertClose(Hips + new Vector3(0.04f, 0f, 0f), p[0], Tolerance, "pushed straight away from the centre");
            AssertClose(Hips + new Vector3(0f, 0f, -0.085f), p[1], 1e-4f, "half the push at half strength");
            AssertClose(start[2], p[2], 0f, "outside the radius nothing moves");
            Assert.AreEqual(2, report.MovedVertices);
            Assert.AreEqual(0.02f, report.MaxMove, 1e-5f);

            push.amount = -0.01f;
            p = (Vector3[])start.Clone();
            UMARigEffectorDeformer.Apply(new[] { push }, rig, null, p, null, null, null);
            AssertClose(Hips + new Vector3(0.01f, 0f, 0f), p[0], Tolerance, "a negative distance pulls in");

            UMARigEffector move = Sphere(0.1f, 0.5f);
            move.action = UMARigEffectorAction.Move;
            move.move = new Vector3(0f, 0.01f, -0.03f);
            p = (Vector3[])start.Clone();
            UMARigEffectorDeformer.Apply(new[] { move }, rig, null, p, null, null, null);
            AssertClose(start[0] + move.move, p[0], Tolerance, "moved");
            AssertClose(start[1] + move.move * 0.5f, p[1], 1e-4f, "moved half way");
            AssertClose(start[2], p[2], 0f, "not moved");

            UMARigEffector scale = Sphere(0.1f, 0.5f);
            scale.action = UMARigEffectorAction.Scale;
            scale.scale = new Vector3(2f, 1f, 1f);
            p = (Vector3[])start.Clone();
            UMARigEffectorDeformer.Apply(new[] { scale }, rig, null, p, null, null, null);
            AssertClose(Hips + new Vector3(0.04f, 0f, 0f), p[0], Tolerance, "twice as far from the centre along x");
            AssertClose(start[1], p[1], Tolerance, "on the yz plane an x scale changes nothing");

            // Switched off, or with nothing to do, an effector is passed over.
            move.enabled = false;
            p = (Vector3[])start.Clone();
            Assert.IsFalse(UMARigEffectorDeformer.Apply(new[] { move }, rig, null, p, null, null, null));
            scale.scale = Vector3.one;
            Assert.IsFalse(scale.IsActive);

            // Effectors run in order, each on what the one before left.
            move.enabled = true;
            move.move = new Vector3(0f, 0f, 0.5f);          // carries the first vertex out of the push's reach
            push.amount = 0.02f;
            p = (Vector3[])start.Clone();
            UMARigEffectorDeformer.Apply(new[] { move, push }, rig, null, p, null, null, null);
            AssertClose(start[0] + move.move, p[0], Tolerance, "moved away first, so the push never sees it");
        }

        [Test]
        public void AMirroredEffectorActsOnBothSidesAsMirrorImages()
        {
            UMARigRestPose rig = Rig();
            UMARigEffector effector = Sphere(0.1f, 0f);
            effector.position = new Vector3(-0.15f, 0f, 0f);
            effector.action = UMARigEffectorAction.Move;
            effector.move = new Vector3(-0.01f, 0.02f, 0.03f);
            Vector3 left = Hips + new Vector3(-0.15f, 0.01f, 0.02f);
            Vector3 right = new Vector3(-left.x, left.y, left.z);

            Vector3[] p = { left, right };
            UMARigEffectorDeformer.Apply(new[] { effector }, rig, null, p, null, null, null);
            AssertClose(left + effector.move, p[0], Tolerance, "its own side");
            AssertClose(right, p[1], 0f, "not mirrored: the other side is untouched");

            effector.mirror = true;
            p = new[] { left, right };
            UMARigEffectorDeformer.Apply(new[] { effector }, rig, null, p, null, null, null);
            AssertClose(left + effector.move, p[0], Tolerance, "its own side");
            AssertClose(right + new Vector3(0.01f, 0.02f, 0.03f), p[1], Tolerance, "the other side moves as the mirror image");
        }

        [Test]
        public void NormalsAndTangentsTurnWithTheSurface()
        {
            UMARigRestPose rig = Rig();
            Sheet(8, 0.4f, 1f, out Vector3[] positions, out Vector3[] normals, out int[] triangles);
            var tangents = new Vector4[positions.Length];
            for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(1f, 0f, 0f, -1f);

            // A bump in the middle of the sheet, fading to nothing 15 cm out.
            UMARigEffector bump = Sphere(0.15f, 1f);
            bump.action = UMARigEffectorAction.Move;
            bump.move = new Vector3(0f, 0.05f, 0f);
            var start = (Vector3[])positions.Clone();
            Assert.IsTrue(UMARigEffectorDeformer.Apply(new[] { bump }, rig, null, positions, normals, tangents, new[] { triangles }));

            int centre = 4 * 9 + 4, east = 4 * 9 + 6, west = 4 * 9 + 2, corner = 0;
            Assert.AreEqual(1.05f, positions[centre].y, 1e-4f, "the middle rose the full amount");
            Assert.That(positions[east].y, Is.InRange(1.001f, 1.049f), "the slope rose part of the way");
            Assert.AreEqual(start[corner], positions[corner], "the corner is outside");

            Assert.That(Vector3.Angle(Vector3.up, normals[centre]), Is.LessThan(1f), "the top of the bump still faces up");
            Assert.That(normals[east].x, Is.GreaterThan(0.1f), "east of the top, the surface leans east");
            Assert.That(normals[west].x, Is.LessThan(-0.1f), "west of the top, it leans west");
            Assert.AreEqual(Vector3.up, normals[corner], "untouched surface keeps its normal exactly");
            Assert.That(tangents[east].y, Is.LessThan(-0.1f), "the tangent runs down the eastern slope");
            Assert.That(tangents[west].y, Is.GreaterThan(0.1f), "and up the western one");
            Assert.AreEqual(-1f, tangents[east].w, "handedness is kept");
            for (int i = 0; i < normals.Length; i++)
            {
                Assert.AreEqual(1f, normals[i].magnitude, 1e-4f, "unit normal " + i);
                Assert.That(Mathf.Abs(Vector3.Dot(normals[i], new Vector3(tangents[i].x, tangents[i].y, tangents[i].z))), Is.LessThan(1e-3f), "tangent stays in the surface, vertex " + i);
            }
        }

        // --- clear the body ----------------------------------------------------------------------

        [Test]
        public void TheBodySurfaceFindsTheNearestSkinAndWhichWayItFaces()
        {
            Sheet(10, 1f, 1f, out Vector3[] skin, out Vector3[] skinNormals, out int[] skinTriangles);
            var body = new UMARigBodySurface();
            Assert.IsFalse(body.TryGetClosest(Vector3.zero, 1f, out _, out _), "nothing in it yet");
            body.Add(skin, skinNormals, skinTriangles);
            Assert.AreEqual(200, body.TriangleCount);

            Assert.IsTrue(body.TryGetClosest(new Vector3(0.123f, 1.04f, -0.31f), 0.1f, out Vector3 closest, out Vector3 normal));
            AssertClose(new Vector3(0.123f, 1f, -0.31f), closest, Tolerance, "straight below, wherever that falls in a triangle");
            AssertClose(Vector3.up, normal, Tolerance, "skin normal");
            Assert.IsTrue(body.TryGetClosest(new Vector3(0.123f, 0.97f, -0.31f), 0.1f, out closest, out normal), "from underneath too");
            AssertClose(new Vector3(0.123f, 1f, -0.31f), closest, Tolerance, "straight above");
            AssertClose(Vector3.up, normal, Tolerance, "the normal still says which way is out");
            Assert.IsFalse(body.TryGetClosest(new Vector3(0f, 1.2f, 0f), 0.1f, out _, out _), "further than asked for");
            Assert.IsTrue(body.TryGetClosest(new Vector3(0.56f, 1f, 0f), 0.1f, out closest, out _), "beside the edge");
            AssertClose(new Vector3(0.5f, 1f, 0f), closest, Tolerance, "the edge itself");
        }

        [Test]
        public void ClearTheBodyBringsBuriedClothOutAndLeavesTheRestAlone()
        {
            UMARigRestPose rig = Rig();
            Sheet(10, 1f, 1f, out Vector3[] skin, out Vector3[] skinNormals, out int[] skinTriangles);
            var body = new UMARigBodySurface();
            body.Add(skin, skinNormals, skinTriangles);

            // Cloth: a sheet 2 cm over the skin, with a dent in the middle that goes 3 cm under it, one
            // vertex 20 cm under (deeper than the effector reaches), and one only 2 mm over.
            Sheet(8, 0.4f, 1.02f, out Vector3[] cloth, out Vector3[] clothNormals, out int[] clothTriangles);
            int[] dent = { 4 * 9 + 4, 4 * 9 + 5, 5 * 9 + 4, 5 * 9 + 5 };
            foreach (int v in dent) cloth[v].y = 0.97f;
            int deep = 1 * 9 + 1, close = 7 * 9 + 7, far = 0;
            cloth[deep].y = 0.8f;
            cloth[close].y = 1.002f;
            var start = (Vector3[])cloth.Clone();

            UMARigEffector clear = Sphere(0.6f, 0f);
            clear.action = UMARigEffectorAction.ClearBody;
            clear.amount = 0.01f;
            clear.reach = 0.08f;
            clear.smoothing = 0;

            // Without the body there is nothing to measure against.
            var report = new UMARigEffectorReport();
            Assert.IsFalse(UMARigEffectorDeformer.Apply(new[] { clear }, rig, null, cloth, clothNormals, null, new[] { clothTriangles }, report));
            Assert.AreEqual(1, report.Warnings.Count);
            StringAssert.Contains("no body surface", report.Warnings[0]);
            CollectionAssert.AreEqual(start, cloth);

            report = new UMARigEffectorReport();
            Assert.IsTrue(UMARigEffectorDeformer.Apply(new[] { clear }, rig, body, cloth, clothNormals, null, new[] { clothTriangles }, report));
            foreach (int v in dent)
            {
                AssertClose(new Vector3(start[v].x, 1.01f, start[v].z), cloth[v], Tolerance, "buried cloth comes out to the clearance, straight along the skin's normal");
            }
            AssertClose(new Vector3(start[close].x, 1.01f, start[close].z), cloth[close], Tolerance, "cloth closer than the clearance is lifted to it");
            AssertClose(start[deep], cloth[deep], 0f, "cloth deeper than the reach is left alone");
            AssertClose(start[far], cloth[far], 0f, "cloth already clear of the skin is left alone");
            Assert.AreEqual(dent.Length + 1, report.MovedVertices);
            Assert.AreEqual(0.04f, report.MaxMove, 1e-5f);

            // Smoothing spreads the push into the cloth around, and never takes any of it away.
            cloth = (Vector3[])start.Clone();
            clear.smoothing = 2;
            UMARigEffectorDeformer.Apply(new[] { clear }, rig, body, cloth, null, null, new[] { clothTriangles });
            foreach (int v in dent)
            {
                Assert.That(cloth[v].y, Is.GreaterThanOrEqualTo(1.01f - Tolerance), "still clear of the skin");
            }
            int beside = 4 * 9 + 3;
            Assert.That(cloth[beside].y, Is.GreaterThan(start[beside].y + 0.001f), "the cloth beside the dent rises with it");
            Assert.That(cloth[beside].y - start[beside].y, Is.LessThan(0.04f), "by less than the dent itself");
            AssertClose(start[far], cloth[far], 0f, "four vertices away nothing has changed");
            for (int v = 0; v < cloth.Length; v++)
            {
                Assert.That(cloth[v].y, Is.GreaterThanOrEqualTo(start[v].y - Tolerance), "nothing is pulled toward the skin, vertex " + v);
            }

            // A hard edge at the effector's radius: outside it the dent stays.
            cloth = (Vector3[])start.Clone();
            clear.smoothing = 0;
            clear.radius = 0.05f;
            clear.position = new Vector3(0.2f, 0f, 0.2f);
            UMARigEffectorDeformer.Apply(new[] { clear }, rig, body, cloth, null, null, new[] { clothTriangles });
            foreach (int v in dent) AssertClose(start[v], cloth[v], 0f, "outside the effector");
        }

        // --- in the profile ------------------------------------------------------------------------

        [Test]
        public void EffectorsBelongToAllSlotsOrToOne()
        {
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            Assert.IsFalse(profile.HasAnyEffectors("Pants"));
            Assert.IsNull(profile.GetEffectorList("Pants"));
            Assert.AreSame(profile.effectors, profile.GetEffectorList(null));

            var general = new UMARigEffector { name = "general", action = UMARigEffectorAction.Move, move = Vector3.up };
            var off = new UMARigEffector { name = "off", enabled = false, action = UMARigEffectorAction.ClearBody };
            profile.effectors.Add(general);
            profile.effectors.Add(off);
            var seat = new UMARigEffector { name = "seat", action = UMARigEffectorAction.ClearBody };
            profile.GetEffectorList("Pants", true).Add(seat);

            var found = new List<UMARigEffector>();
            profile.CollectEffectors("Pants", found);
            CollectionAssert.AreEqual(new[] { general, seat }, found, "all-slot effectors first, then the slot's own; switched-off ones left out");
            found.Clear();
            profile.CollectEffectors("Shirt", found);
            CollectionAssert.AreEqual(new[] { general }, found);

            Assert.IsTrue(profile.NeedsBodySurface("Pants"));
            Assert.IsFalse(profile.NeedsBodySurface("Shirt"), "the switched-off one does not count");
            Assert.IsTrue(profile.HasClearBodyEffectors);

            // A slot that holds only effectors is kept when its bone entries go.
            profile.GetOrAddCorrection("Hips", "Pants");
            Assert.IsTrue(profile.RemoveCorrection("Hips", "Pants"));
            Assert.IsNotNull(profile.GetSlotOverride("Pants"), "the slot still has its effector");
            UMASlotRigConversionProfile copy = Object.Instantiate(profile);
            created.Add(copy);
            Assert.AreEqual(1, copy.GetEffectorList("Pants").Count, "effectors are saved with the profile");
            Assert.AreEqual("seat", copy.GetEffectorList("Pants")[0].name);

            profile.GetEffectorList("Pants").Clear();
            profile.RemoveSlotOverrideIfEmpty("Pants");
            Assert.IsNull(profile.GetSlotOverride("Pants"));
            Assert.IsFalse(profile.HasClearBodyEffectors);
        }

        [Test]
        public void AnEffectorCanBeMovedBetweenAllSlotsAndOneSlot()
        {
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            var effector = new UMARigEffector { name = "seat", action = UMARigEffectorAction.Move, move = Vector3.up };
            profile.GetEffectorList("Pants", true).Add(effector);
            var found = new List<UMARigEffector>();

            Assert.IsTrue(profile.TryGetEffectorSlot(effector, out string slot));
            Assert.AreEqual("Pants", slot);
            profile.CollectEffectors("Jacket", found);
            Assert.AreEqual(0, found.Count, "kept to the pants, it leaves the jacket alone wherever the jacket is");

            // All slots: whatever the character wears, so the profile can be reused.
            Assert.IsTrue(profile.SetEffectorSlot(effector, null));
            Assert.IsTrue(profile.TryGetEffectorSlot(effector, out slot));
            Assert.IsNull(slot);
            CollectionAssert.AreEqual(new[] { effector }, profile.effectors);
            Assert.IsNull(profile.GetSlotOverride("Pants"), "the slot's block held nothing else, so it goes");
            foreach (string worn in new[] { "Pants", "Jacket", "A slot this profile has never seen" })
            {
                found.Clear();
                profile.CollectEffectors(worn, found);
                CollectionAssert.AreEqual(new[] { effector }, found, worn);
                Assert.IsTrue(profile.HasAnyEffectors(worn), worn);
            }

            // From one slot to another. A slot with a bone entry of its own keeps its block.
            profile.GetOrAddCorrection("Hips", "Pants");
            Assert.IsTrue(profile.SetEffectorSlot(effector, "Pants"));
            Assert.AreEqual(0, profile.effectors.Count);
            Assert.IsTrue(profile.SetEffectorSlot(effector, "Jacket"));
            Assert.IsNotNull(profile.GetSlotOverride("Pants"), "the pants keep their bone entry");
            Assert.AreEqual(0, profile.GetEffectorList("Pants").Count);
            Assert.IsTrue(profile.TryGetEffectorSlot(effector, out slot));
            Assert.AreEqual("Jacket", slot);
            Assert.IsFalse(profile.HasAnyEffectors("Pants"));
            Assert.IsTrue(profile.HasAnyEffectors("Jacket"));

            // Setting what it already has changes nothing; an empty name means all slots.
            Assert.IsTrue(profile.SetEffectorSlot(effector, "Jacket"));
            Assert.AreEqual(1, profile.GetEffectorList("Jacket").Count);
            Assert.IsTrue(profile.SetEffectorSlot(effector, string.Empty));
            Assert.IsTrue(profile.TryGetEffectorSlot(effector, out slot));
            Assert.IsNull(slot);
            Assert.IsNull(profile.GetSlotOverride("Jacket"));
            Assert.AreEqual(1, profile.effectors.Count, "moved, never copied");

            // An effector the profile does not hold.
            var stranger = new UMARigEffector();
            Assert.IsFalse(profile.TryGetEffectorSlot(stranger, out _));
            Assert.IsFalse(profile.SetEffectorSlot(stranger, "Pants"));
            Assert.IsFalse(profile.TryGetEffectorSlot(null, out _));
            Assert.AreEqual(0, profile.GetEffectorList("Pants").Count);
        }

        [Test]
        public void CountInsideCountsThePointsAnEffectorAndItsMirrorImageHold()
        {
            UMARigRestPose rig = Rig();
            UMARigEffector effector = Sphere(0.1f, 0.5f);
            effector.position = new Vector3(0.3f, 0f, 0f);
            var points = new[]
            {
                Hips + new Vector3(0.3f, 0f, 0f),     // its centre
                Hips + new Vector3(0.35f, 0f, 0.05f), // in the fade
                Hips + new Vector3(-0.3f, 0f, 0f),    // the centre of its mirror image
                Hips,                                 // between the two
                Hips + new Vector3(0.3f, 0.12f, 0f),  // just outside
            };
            Assert.AreEqual(2, effector.CountInside(points, rig));
            effector.mirror = true;
            Assert.AreEqual(3, effector.CountInside(points, rig), "the mirror image holds one more");
            effector.enabled = false;
            Assert.AreEqual(3, effector.CountInside(points, rig), "this is about where it is, not whether it is switched on");
            effector.radius = 0f;
            Assert.AreEqual(0, effector.CountInside(points, rig));
            Assert.AreEqual(0, Sphere(0.1f, 0.5f).CountInside(null, rig));

            // On the centre line an effector and its mirror image are the same place: a point counts once.
            UMARigEffector central = Sphere(0.1f, 0.5f);
            central.mirror = true;
            Assert.AreEqual(1, central.CountInside(new[] { Hips }, rig));
        }
    }
}
