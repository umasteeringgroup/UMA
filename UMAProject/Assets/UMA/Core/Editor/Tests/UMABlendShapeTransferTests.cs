using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Blend shape transfer on hand-made geometry: binding, falloff, following a turning surface,
    /// seams, clearance, rigid parts, mesh-space mapping, normals, and writing to a slot.
    /// </summary>
    public sealed class UMABlendShapeTransferTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            created.Clear();
        }

        // --- geometry --------------------------------------------------------------------------------

        /// <summary>A flat grid in the XZ plane, centred on the origin, facing +y.</summary>
        private static void Grid(int cells, float size, float height, out Vector3[] positions, out int[] triangles)
        {
            int side = cells + 1;
            positions = new Vector3[side * side];
            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    positions[z * side + x] = new Vector3((x / (float)cells - 0.5f) * size, height, (z / (float)cells - 0.5f) * size);
                }
            }
            var list = new List<int>();
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int a = z * side + x, b = a + side, c = a + 1, d = b + 1;
                    list.AddRange(new[] { a, b, c, c, b, d });
                }
            }
            triangles = list.ToArray();
        }

        private static Vector3[] Up(int count)
        {
            var normals = new Vector3[count];
            for (int i = 0; i < count; i++) normals[i] = Vector3.up;
            return normals;
        }

        private delegate Vector3 Movement(Vector3 position);

        /// <summary>A one-metre source plane with blend shapes given as movement per position.</summary>
        private static UMABlendShapeSource Source(params (string name, Movement move)[] shapes)
        {
            Grid(20, 1f, 0f, out Vector3[] positions, out int[] triangles);
            var blendShapes = new UMABlendShape[shapes.Length];
            for (int s = 0; s < shapes.Length; s++)
            {
                var deltas = new Vector3[positions.Length];
                for (int v = 0; v < deltas.Length; v++) deltas[v] = shapes[s].move(positions[v]);
                blendShapes[s] = new UMABlendShape
                {
                    shapeName = shapes[s].name,
                    frames = new[] { new UMABlendFrame { frameWeight = 100f, deltaVertices = deltas, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] } },
                };
            }
            var mesh = new UMAMeshData { vertices = positions, vertexCount = positions.Length, blendShapes = blendShapes };
            var source = new UMABlendShapeSource();
            source.Add(UMABlendShapeSlotSpace.FromArrays("Body", positions, Up(positions.Length), triangles, mesh));
            source.Build();
            return source;
        }

        /// <summary>A small flat patch of cloth at a height over the source plane.</summary>
        private static UMABlendShapeSlotSpace Cloth(float height, float size = 0.4f, int cells = 8)
        {
            Grid(cells, size, height, out Vector3[] positions, out int[] triangles);
            return UMABlendShapeSlotSpace.FromArrays("Cloth", positions, Up(positions.Length), triangles);
        }

        /// <summary>Settings with every refinement off: what comes out is the bare binding.</summary>
        private static UMABlendShapeTransferSettings Bare()
        {
            return new UMABlendShapeTransferSettings
            {
                method = UMABlendShapeTransferMethod.CopyMovement,
                useFalloff = false,
                smoothingIterations = 0,
                keepClearance = false,
                minimumMovement = 0f,
                skipShapeBelow = 0f,
                normals = UMABlendShapeNormalMode.PositionsOnly,
            };
        }

        private static readonly Movement Bump = p => new Vector3(0f, 0.05f * Mathf.Max(0f, 1f - p.magnitude / 0.3f), 0f);
        private static readonly Movement Lift = p => new Vector3(0f, 0.05f, 0f);
        private static readonly Movement Tilt = p => new Vector3(0f, 0.2f * p.x, 0f);

        // --- lookups ---------------------------------------------------------------------------------

        [Test]
        public void WeldingGroupsVerticesAtTheSamePlaceOnly()
        {
            var positions = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 0.00001f), new Vector3(1f, 0.001f, 0f), new Vector3(1f, 0f, 0f),
            };
            int[] groups = UMABlendShapeTransfer.WeldByPosition(positions, UMABlendShapeTransfer.WeldTolerance, out int count);
            Assert.AreEqual(3, count);
            Assert.AreEqual(groups[0], groups[2], "10 micrometres apart is the same point");
            Assert.AreEqual(groups[1], groups[4]);
            Assert.AreNotEqual(groups[1], groups[3], "a millimetre apart is not");
        }

        [Test]
        public void TheLookupFindsTheSameNearestTriangleAsABruteForceSearch()
        {
            var random = new System.Random(4321);
            float R() => (float)random.NextDouble();
            var positions = new Vector3[600];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector3(R(), R(), R());
            var triangles = new int[positions.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
            var lookup = new UMATriangleLookup(positions, null, triangles);
            for (int q = 0; q < 300; q++)
            {
                var p = new Vector3(R() * 1.8f - 0.4f, R() * 1.8f - 0.4f, R() * 1.8f - 0.4f);
                float brute = float.MaxValue;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    brute = Mathf.Min(brute, (p - UMATriangleLookup.ClosestPointOnTriangle(p, positions[t], positions[t + 1], positions[t + 2])).magnitude);
                }
                Assert.IsTrue(lookup.FindNearest(p, Vector3.zero, 0f, out _, out Vector3 closest, out float distance));
                Assert.AreEqual(brute, distance, 1e-5f, $"query {q}");
                Assert.AreEqual(distance, (p - closest).magnitude, 1e-5f);
            }
        }

        [Test]
        public void ASurfaceFacingAwayIsChosenOnlyWhenMuchNearer()
        {
            // Two sheets 2 cm apart: the lower faces down, the upper faces up. A point just above the lower one, facing up.
            var positions = new[]
            {
                new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0.02f, -1f), new Vector3(0f, 0.02f, 1f), new Vector3(1f, 0.02f, -1f),
            };
            var normals = new[] { Vector3.down, Vector3.down, Vector3.down, Vector3.up, Vector3.up, Vector3.up };
            var lookup = new UMATriangleLookup(positions, normals, new[] { 0, 1, 2, 3, 4, 5 });
            var point = new Vector3(0f, 0.005f, 0f);
            lookup.FindNearest(point, Vector3.up, 0.03f, out int withPenalty, out _, out float distance);
            Assert.AreEqual(1, withPenalty, "the sheet facing the same way wins");
            Assert.AreEqual(0.015f, distance, 1e-5f, "and the distance reported is the real one");
            lookup.FindNearest(point, Vector3.up, 0f, out int without, out _, out _);
            Assert.AreEqual(0, without);
        }

        // --- the transfer ----------------------------------------------------------------------------

        [Test]
        public void ClothTakesTheMovementOfTheSurfaceUnderIt()
        {
            UMABlendShapeSource source = Source(("Bump", Bump));
            UMABlendShapeSlotSpace cloth = Cloth(0.01f);
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Bump" }, Bare());

            UMABlendShapeTransferResult.ShapeResult shape = result.Find("Bump");
            Assert.NotNull(shape.Shape);
            Assert.AreEqual(cloth.VertexCount, result.BoundVertices);
            for (int v = 0; v < cloth.VertexCount; v++)
            {
                Vector3 p = cloth.Positions[v];
                Vector3 expected = Bump(new Vector3(p.x, 0f, p.z));
                Assert.AreEqual(expected.y, shape.AvatarDeltas[v].y, 0.0015f, $"vertex {v}");
                Assert.AreEqual(0f, shape.AvatarDeltas[v].x, 1e-6f);
                // Mesh space is avatar space here.
                Assert.AreEqual(shape.AvatarDeltas[v], shape.Shape.frames[0].deltaVertices[v]);
            }
            Assert.AreEqual(0.05f, shape.MaxMovement, 0.001f);
            Assert.AreEqual(100f, shape.Shape.frames[0].frameWeight);
            Assert.IsFalse(shape.Shape.frames[0].HasNormals());
        }

        [Test]
        public void MovementFadesWithDistanceFromTheSurface()
        {
            UMABlendShapeSource source = Source(("Lift", Lift));
            UMABlendShapeTransferSettings settings = Bare();
            settings.useFalloff = true;
            settings.fullStrengthDistance = 0.03f;
            settings.fadeOutDistance = 0.12f;

            float Moved(float height)
            {
                UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, Cloth(height), new[] { "Lift" }, settings);
                UMABlendShapeTransferResult.ShapeResult shape = result.Find("Lift");
                return shape.Shape != null ? shape.MaxMovement : 0f;
            }

            Assert.AreEqual(0.05f, Moved(0.01f), 1e-5f, "inside the full-strength distance");
            Assert.AreEqual(0.025f, Moved(0.075f), 1e-4f, "half way through the fade");
            Assert.AreEqual(0f, Moved(0.2f), 1e-6f, "beyond the fade");
            Assert.AreEqual(1f, settings.Influence(0.03f), 1e-6f);
            Assert.AreEqual(0f, settings.Influence(0.12f), 1e-6f);
        }

        [Test]
        public void StrengthScalesTheMovement()
        {
            UMABlendShapeSource source = Source(("Lift", Lift));
            UMABlendShapeTransferSettings settings = Bare();
            settings.strength = 0.5f;
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, Cloth(0.01f), new[] { "Lift" }, settings);
            Assert.AreEqual(0.025f, result.Find("Lift").MaxMovement, 1e-5f);
        }

        [Test]
        public void FollowingTheSurfaceTurnsTheOffsetWithIt()
        {
            UMABlendShapeSource source = Source(("Tilt", Tilt));
            UMABlendShapeSlotSpace cloth = Cloth(0.02f, 0.2f, 4);
            UMABlendShapeTransferSettings settings = Bare();

            UMABlendShapeTransferResult copied = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Tilt" }, settings);
            settings.method = UMABlendShapeTransferMethod.FollowSurface;
            UMABlendShapeTransferResult followed = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Tilt" }, settings);

            // The plane tilts by atan(0.2); a point 2 cm above it swings sideways by 2 cm x sin of that angle.
            float angle = Mathf.Atan(0.2f);
            float sideways = -0.02f * Mathf.Sin(angle);
            float down = 0.02f * (Mathf.Cos(angle) - 1f);
            for (int v = 0; v < cloth.VertexCount; v++)
            {
                Vector3 copy = copied.Find("Tilt").AvatarDeltas[v];
                Vector3 follow = followed.Find("Tilt").AvatarDeltas[v];
                Assert.AreEqual(0f, copy.x, 1e-6f, "copying the movement never moves sideways here");
                Assert.AreEqual(0.2f * cloth.Positions[v].x, copy.y, 1e-5f);
                Assert.AreEqual(sideways, follow.x, 2e-4f, $"vertex {v}");
                Assert.AreEqual(copy.y + down, follow.y, 2e-4f, $"vertex {v}");
            }
        }

        [Test]
        public void VerticesThatShareAPlaceMoveTogether()
        {
            // Two quads meeting along x = 0, with the seam's vertices split as a UV seam splits them.
            var positions = new[]
            {
                new Vector3(-0.1f, 0.01f, -0.1f), new Vector3(-0.1f, 0.01f, 0.1f), new Vector3(0f, 0.01f, -0.1f), new Vector3(0f, 0.01f, 0.1f),
                new Vector3(0f, 0.01f, -0.1f), new Vector3(0f, 0.01f, 0.1f), new Vector3(0.1f, 0.01f, -0.1f), new Vector3(0.1f, 0.01f, 0.1f),
            };
            int[] triangles = { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 };
            UMABlendShapeSlotSpace cloth = UMABlendShapeSlotSpace.FromArrays("Seam", positions, Up(8), triangles);
            UMABlendShapeTransferSettings settings = Bare();
            settings.smoothingIterations = 5;
            settings.smoothingStrength = 0.6f;
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(Source(("Bump", Bump)), cloth, new[] { "Bump" }, settings);

            Vector3[] deltas = result.Find("Bump").AvatarDeltas;
            Assert.AreEqual(deltas[2], deltas[4]);
            Assert.AreEqual(deltas[3], deltas[5]);
            Assert.Greater(deltas[2].y, 0.01f);
        }

        [Test]
        public void SmoothingEvensOutTheMovement()
        {
            UMABlendShapeSource source = Source(("Bump", Bump));
            UMABlendShapeSlotSpace cloth = Cloth(0.01f, 0.8f, 16);
            UMABlendShapeTransferSettings settings = Bare();
            float peakBefore = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Bump" }, settings).Find("Bump").MaxMovement;
            settings.smoothingIterations = 10;
            settings.smoothingStrength = 0.5f;
            float peakAfter = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Bump" }, settings).Find("Bump").MaxMovement;
            Assert.Less(peakAfter, peakBefore);
            Assert.Greater(peakAfter, peakBefore * 0.4f);
        }

        [Test]
        public void ClothThatWouldEndUnderTheSurfaceIsKeptClear()
        {
            UMABlendShapeSource source = Source(("Lift", Lift));
            UMABlendShapeSlotSpace cloth = Cloth(0.005f);
            UMABlendShapeTransferSettings settings = Bare();
            settings.strength = 0.5f;   // the cloth rises 2.5 cm, the surface 5 cm

            UMABlendShapeTransferResult.ShapeResult under = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Lift" }, settings).Find("Lift");
            Assert.AreEqual(cloth.VertexCount, under.PokeThrough);
            Assert.AreEqual(0, under.ClearanceFixed);
            Assert.IsTrue(under.Under[0]);

            settings.keepClearance = true;
            settings.clearance = 0.001f;
            UMABlendShapeTransferResult.ShapeResult kept = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Lift" }, settings).Find("Lift");
            Assert.AreEqual(0, kept.PokeThrough);
            Assert.AreEqual(cloth.VertexCount, kept.ClearanceFixed);
            // 5 mm above the surface at rest; 1 mm above it afterwards: the cloth rises 4.6 cm.
            Assert.AreEqual(0.046f, kept.AvatarDeltas[0].y, 1e-4f);

            // A gap smaller than the clearance is kept as it was, not widened.
            settings.clearance = 0.02f;
            UMABlendShapeTransferResult.ShapeResult tight = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Lift" }, settings).Find("Lift");
            Assert.AreEqual(0.05f, tight.AvatarDeltas[0].y, 1e-4f);
        }

        [Test]
        public void ClothAlreadyUnderTheSurfaceIsReportedAndLeftWhereItIs()
        {
            UMABlendShapeSource source = Source(("Lift", Lift));
            UMABlendShapeTransferSettings settings = Bare();
            settings.keepClearance = true;
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, Cloth(-0.004f), new[] { "Lift" }, settings);
            Assert.AreEqual(result.Space.VertexCount, result.UnderAtRestCount);
            Assert.AreEqual(0, result.Find("Lift").PokeThrough, "it was not outside to begin with");
            Assert.AreEqual(0.05f, result.Find("Lift").AvatarDeltas[0].y, 1e-5f, "it still follows the surface");
        }

        [Test]
        public void ASmallSeparatePartMovesAsOnePiece()
        {
            // A large sheet and, beside it, a 2 cm stud over the slope of the bump.
            Grid(8, 0.4f, 0.01f, out Vector3[] sheet, out int[] sheetTriangles);
            var positions = new List<Vector3>(sheet);
            int start = positions.Count;
            positions.AddRange(new[]
            {
                new Vector3(0.10f, 0.03f, 0f), new Vector3(0.10f, 0.03f, 0.02f), new Vector3(0.12f, 0.03f, 0f), new Vector3(0.12f, 0.03f, 0.02f),
            });
            var triangles = new List<int>(sheetTriangles);
            triangles.AddRange(new[] { start, start + 1, start + 2, start + 2, start + 1, start + 3 });
            UMABlendShapeSlotSpace cloth = UMABlendShapeSlotSpace.FromArrays("Studded", positions.ToArray(), Up(positions.Count), triangles.ToArray());

            UMABlendShapeTransferSettings settings = Bare();
            Vector3[] loose = UMABlendShapeTransfer.Transfer(Source(("Bump", Bump)), cloth, new[] { "Bump" }, settings).Find("Bump").AvatarDeltas;
            Assert.AreNotEqual(loose[start].y, loose[start + 2].y, "on a slope the stud's corners move differently");

            settings.rigidSmallParts = true;
            settings.rigidPartMaxSize = 0.05f;
            Vector3[] rigid = UMABlendShapeTransfer.Transfer(Source(("Bump", Bump)), cloth, new[] { "Bump" }, settings).Find("Bump").AvatarDeltas;
            Assert.AreEqual(rigid[start], rigid[start + 1]);
            Assert.AreEqual(rigid[start], rigid[start + 2]);
            Assert.AreEqual(rigid[start], rigid[start + 3]);
            float average = (loose[start].y + loose[start + 1].y + loose[start + 2].y + loose[start + 3].y) / 4f;
            Assert.AreEqual(average, rigid[start].y, 1e-6f);
            // The sheet is far larger than 5 cm and is not made rigid.
            Assert.AreNotEqual(rigid[0].y, rigid[40].y);
        }

        [Test]
        public void DeltasAreWrittenInTheSlotsOwnMeshSpace()
        {
            UMABlendShapeSlotSpace cloth = Cloth(0.01f);
            // A slot whose mesh space is turned and scaled against avatar space.
            Matrix4x4 toAvatar = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 30f, 0f), new Vector3(2f, 2f, 2f));
            for (int v = 0; v < cloth.ToAvatar.Length; v++) cloth.ToAvatar[v] = toAvatar;

            UMABlendShapeTransferResult.ShapeResult shape = UMABlendShapeTransfer.Transfer(Source(("Lift", Lift)), cloth, new[] { "Lift" }, Bare()).Find("Lift");
            for (int v = 0; v < cloth.VertexCount; v++)
            {
                Vector3 back = toAvatar.MultiplyVector(shape.Shape.frames[0].deltaVertices[v]);
                Assert.AreEqual(0f, (back - shape.AvatarDeltas[v]).magnitude, 1e-6f);
                Assert.AreEqual(0.025f, shape.Shape.frames[0].deltaVertices[v].magnitude, 1e-5f, "half the size in a space scaled by two");
            }
        }

        [Test]
        public void NormalDeltasFollowTheBendOfTheSlot()
        {
            UMABlendShapeSource source = Source(("Tilt", Tilt), ("Lift", Lift));
            UMABlendShapeSlotSpace cloth = Cloth(0.01f, 0.2f, 4);
            UMABlendShapeTransferSettings settings = Bare();
            settings.normals = UMABlendShapeNormalMode.Recalculate;
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Tilt", "Lift" }, settings);

            UMABlendFrame tilt = result.Find("Tilt").Shape.frames[0];
            Assert.IsTrue(tilt.HasNormals());
            Vector3 expected = new Vector3(-0.2f, 1f, 0f).normalized - Vector3.up;
            for (int v = 0; v < cloth.VertexCount; v++)
            {
                Assert.AreEqual(0f, (tilt.deltaNormals[v] - expected).magnitude, 1e-3f, $"vertex {v}");
            }
            // A shape that only lifts the slot bends nothing: no normal deltas are stored at all.
            Assert.IsFalse(result.Find("Lift").Shape.frames[0].HasNormals());
        }

        [Test]
        public void AShapeThatDoesNotMoveTheSlotIsNotAdded()
        {
            UMABlendShapeSource source = Source(("Bump", Bump));
            UMABlendShapeTransferSettings settings = new UMABlendShapeTransferSettings();   // the defaults: falloff on
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, Cloth(0.5f), new[] { "Bump", "NotThere" }, settings);
            Assert.IsNull(result.Find("Bump").Shape);
            StringAssert.Contains("does not move", result.Find("Bump").Skipped);
            Assert.IsNull(result.Find("NotThere").Shape);
            StringAssert.Contains("does not have it", result.Find("NotThere").Skipped);
            Assert.AreEqual(0, result.VerticesInReach);
        }

        [Test]
        public void EveryFrameOfAShapeIsTransferred()
        {
            Grid(20, 1f, 0f, out Vector3[] positions, out int[] triangles);
            var half = new Vector3[positions.Length];
            var full = new Vector3[positions.Length];
            for (int v = 0; v < positions.Length; v++)
            {
                half[v] = new Vector3(0f, 0.01f, 0f);
                full[v] = new Vector3(0f, 0.05f, 0f);
            }
            var mesh = new UMAMeshData
            {
                vertices = positions,
                vertexCount = positions.Length,
                blendShapes = new[]
                {
                    new UMABlendShape
                    {
                        shapeName = "Grow",
                        frames = new[]
                        {
                            new UMABlendFrame { frameWeight = 50f, deltaVertices = half, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] },
                            new UMABlendFrame { frameWeight = 100f, deltaVertices = full, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] },
                        },
                    },
                },
            };
            var source = new UMABlendShapeSource();
            source.Add(UMABlendShapeSlotSpace.FromArrays("Body", positions, Up(positions.Length), triangles, mesh));

            UMABlendShape shape = UMABlendShapeTransfer.Transfer(source, Cloth(0.01f), new[] { "Grow" }, Bare()).Find("Grow").Shape;
            Assert.AreEqual(2, shape.frames.Length);
            Assert.AreEqual(50f, shape.frames[0].frameWeight);
            Assert.AreEqual(100f, shape.frames[1].frameWeight);
            Assert.AreEqual(0.01f, shape.frames[0].deltaVertices[0].y, 1e-6f);
            Assert.AreEqual(0.05f, shape.frames[1].deltaVertices[0].y, 1e-6f);
        }

        [Test]
        public void TwoSourceSlotsAreOneSurfaceAndShapesMissingOnOneAreZeroThere()
        {
            // Two half planes side by side; only the left one carries the shape.
            Grid(10, 0.5f, 0f, out Vector3[] left, out int[] triangles);
            Grid(10, 0.5f, 0f, out Vector3[] right, out _);
            for (int v = 0; v < left.Length; v++)
            {
                left[v].x -= 0.25f;
                right[v].x += 0.25f;
            }
            var lift = new Vector3[left.Length];
            for (int v = 0; v < lift.Length; v++) lift[v] = new Vector3(0f, 0.04f, 0f);
            var leftMesh = new UMAMeshData
            {
                vertices = left,
                vertexCount = left.Length,
                blendShapes = new[] { new UMABlendShape { shapeName = "Lift", frames = new[] { new UMABlendFrame { frameWeight = 100f, deltaVertices = lift, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] } } } },
            };
            var source = new UMABlendShapeSource();
            source.Add(UMABlendShapeSlotSpace.FromArrays("Left", left, Up(left.Length), triangles, leftMesh));
            source.Add(UMABlendShapeSlotSpace.FromArrays("Right", right, Up(right.Length), triangles, new UMAMeshData { vertices = right, vertexCount = right.Length }));
            source.Build();
            Assert.AreEqual(1, source.Shapes.Count);
            CollectionAssert.AreEqual(new[] { "Left" }, source.Shapes[0].Slots);
            Assert.Less(source.GroupCount, source.VertexCount, "the seam between the two slots is welded");

            UMABlendShapeSlotSpace cloth = Cloth(0.01f, 0.8f, 16);
            Vector3[] deltas = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Lift" }, Bare()).Find("Lift").AvatarDeltas;
            for (int v = 0; v < cloth.VertexCount; v++)
            {
                float x = cloth.Positions[v].x;
                if (x < -0.06f) Assert.AreEqual(0.04f, deltas[v].y, 1e-5f, $"over the left slot at x={x}");
                if (x > 0.06f) Assert.AreEqual(0f, deltas[v].y, 1e-5f, $"over the right slot at x={x}");
            }
        }

        // --- editing a shape afterwards -----------------------------------------------------------------

        [Test]
        public void AdditionsGoToTheLastFrameInFullAndToEarlierFramesInProportion()
        {
            UMABlendShapeSlotSpace cloth = Cloth(0.01f, 0.2f, 4);
            int count = cloth.VertexCount;
            var half = new Vector3[count];
            var full = new Vector3[count];
            var strokes = new Vector3[count];
            for (int v = 0; v < count; v++)
            {
                half[v] = new Vector3(0f, 0.01f, 0f);
                full[v] = new Vector3(0f, 0.03f, 0f);
            }
            strokes[3] = new Vector3(0.004f, 0f, 0f);
            var existing = new UMABlendShape
            {
                shapeName = "Grow",
                frames = new[]
                {
                    new UMABlendFrame { frameWeight = 50f, deltaVertices = half, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] },
                    new UMABlendFrame { frameWeight = 100f, deltaVertices = full, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] },
                },
            };

            UMABlendShape merged = UMABlendShapeTransfer.AddToShape(existing, "Grow", strokes, null);
            Assert.AreEqual(2, merged.frames.Length);
            Assert.AreEqual(new Vector3(0.002f, 0.01f, 0f), merged.frames[0].deltaVertices[3], "half the stroke at half the weight");
            Assert.AreEqual(new Vector3(0.004f, 0.03f, 0f), merged.frames[1].deltaVertices[3]);
            Assert.AreEqual(full[0], merged.frames[1].deltaVertices[0], "vertices without strokes are unchanged");
            Assert.AreEqual(new Vector3(0f, 0.03f, 0f), existing.frames[1].deltaVertices[3], "the existing shape is not modified");

            // With no shape to add to, the strokes are the shape.
            UMABlendShape created = UMABlendShapeTransfer.AddToShape(null, "New", strokes, null);
            Assert.AreEqual(1, created.frames.Length);
            Assert.AreEqual(100f, created.frames[0].frameWeight);
            Assert.AreEqual(strokes[3], created.frames[0].deltaVertices[3]);
            // A shape made for another vertex count cannot be added to.
            UMABlendShape mismatched = UMABlendShapeTransfer.AddToShape(existing, "Grow", new Vector3[count + 1], null);
            Assert.AreEqual(1, mismatched.frames.Length);
        }

        [Test]
        public void NormalDeltasCanBeWorkedOutForAShapeMadeElsewhere()
        {
            UMABlendShapeSlotSpace cloth = Cloth(0.01f, 0.2f, 4);
            var tilt = new Vector3[cloth.VertexCount];
            var lift = new Vector3[cloth.VertexCount];
            for (int v = 0; v < tilt.Length; v++)
            {
                tilt[v] = new Vector3(0f, 0.2f * cloth.Positions[v].x, 0f);
                lift[v] = new Vector3(0f, 0.02f, 0f);
            }
            var tilted = new UMABlendFrame { frameWeight = 100f, deltaVertices = tilt };
            Assert.IsTrue(UMABlendShapeTransfer.RecalculateNormalDeltas(cloth, tilted));
            Vector3 expected = new Vector3(-0.2f, 1f, 0f).normalized - Vector3.up;
            for (int v = 0; v < tilt.Length; v++)
            {
                Assert.AreEqual(0f, (tilted.deltaNormals[v] - expected).magnitude, 1e-3f);
            }
            var lifted = new UMABlendFrame { frameWeight = 100f, deltaVertices = lift, deltaNormals = new Vector3[tilt.Length] };
            Assert.IsFalse(UMABlendShapeTransfer.RecalculateNormalDeltas(cloth, lifted), "a lift bends nothing");
            Assert.IsFalse(lifted.HasNormals());
            Assert.IsFalse(UMABlendShapeTransfer.RecalculateNormalDeltas(cloth, new UMABlendFrame { deltaVertices = new Vector3[3] }), "wrong vertex count");
        }

        [Test]
        public void ALegacySlotsDeltasMapToTheWornCopyAndBack()
        {
            UMABlendShapeSlotSpace cloth = Cloth(0.01f, 0.2f, 2);
            var delta = new Vector3(0.01f, 0.02f, -0.03f);
            // Any slot that is not worn as a converted copy: the two spaces are the same.
            Assert.AreEqual(delta, cloth.OwnToWornSpace(0, delta));
            Assert.AreEqual(delta, cloth.WornToOwnSpace(0, delta));

            Matrix4x4 conversion = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(20f, 70f, -35f), new Vector3(1.1f, 0.9f, 1.3f));
            cloth.OwnToWorn = new Matrix4x4[cloth.VertexCount];
            for (int v = 0; v < cloth.VertexCount; v++) cloth.OwnToWorn[v] = conversion;
            Vector3 worn = cloth.OwnToWornSpace(1, delta);
            Assert.AreEqual(0f, (worn - conversion.MultiplyVector(delta)).magnitude, 1e-6f);
            Assert.AreEqual(0f, (cloth.WornToOwnSpace(1, worn) - delta).magnitude, 1e-6f);
        }

        // --- writing ---------------------------------------------------------------------------------

        private SlotDataAsset Slot(UMABlendShapeSlotSpace space, params UMABlendShape[] shapes)
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            created.Add(slot);
            slot.name = "TransferTestSlot";
            slot.meshData = new UMAMeshData { vertices = space.Positions, vertexCount = space.VertexCount, blendShapes = shapes };
            return slot;
        }

        [Test]
        public void ApplyAddsReplacesOrKeepsAndRemoveTakesThemOffAgain()
        {
            UMABlendShapeSource source = Source(("Lift", Lift), ("Bump", Bump));
            UMABlendShapeSlotSpace cloth = Cloth(0.01f);
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, cloth, new[] { "Lift", "Bump" }, Bare());

            var own = new UMABlendShape { shapeName = "Lift", frames = new[] { new UMABlendFrame(cloth.VertexCount, false, false) } };
            var other = new UMABlendShape { shapeName = "Other", frames = new[] { new UMABlendFrame(cloth.VertexCount, false, false) } };
            SlotDataAsset slot = Slot(cloth, own, other);

            var log = new List<string>();
            Assert.AreEqual(1, UMABlendShapeTransfer.Apply(result, slot, UMABlendShapeExistingMode.Skip, log));
            Assert.AreEqual(3, slot.meshData.blendShapes.Length);
            Assert.AreSame(own, slot.meshData.blendShapes[0], "the slot's own Lift is kept");
            StringAssert.Contains("1 added", log[0]);
            StringAssert.Contains("1 already there and kept", log[0]);

            Assert.AreEqual(2, UMABlendShapeTransfer.Apply(result, slot, UMABlendShapeExistingMode.Replace));
            Assert.AreEqual(3, slot.meshData.blendShapes.Length);
            Assert.AreSame(result.Find("Lift").Shape, slot.meshData.blendShapes[0], "replaced in place");
            Assert.AreSame(other, slot.meshData.blendShapes[1]);

            Assert.AreEqual(2, UMABlendShapeTransfer.RemoveShapes(slot, new HashSet<string> { "Lift", "Bump", "Missing" }));
            Assert.AreEqual(1, slot.meshData.blendShapes.Length);
            Assert.AreSame(other, slot.meshData.blendShapes[0]);
        }

        [Test]
        public void ApplyRefusesAShapeMadeForAnotherVertexCount()
        {
            UMABlendShapeSource source = Source(("Lift", Lift));
            UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, Cloth(0.01f), new[] { "Lift" }, Bare());
            SlotDataAsset slot = Slot(Cloth(0.01f, 0.4f, 4));
            var log = new List<string>();
            Assert.AreEqual(0, UMABlendShapeTransfer.Apply(result, slot, UMABlendShapeExistingMode.Replace, log));
            StringAssert.Contains("not written", log[0]);
            Assert.IsTrue(slot.meshData.blendShapes == null || slot.meshData.blendShapes.Length == 0);
        }
    }
}
