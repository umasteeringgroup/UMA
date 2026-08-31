#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class CoordinateMappingTests
    {
        [Test]
        public void BarycentricCoordinateMapsToExpectedUV()
        {
            Vector2 uv = TexturePaintMath.BarycentricToUV(Vector2.zero, Vector2.right, Vector2.up,
                new Vector3(0.25f, 0.5f, 0.25f));
            Assert.That(uv.x, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(uv.y, Is.EqualTo(0.25f).Within(0.00001f));
        }

        [Test]
        public void StrokeCoverageStopsAtOneHundredPercent()
        {
            float coverage = 0f;

            float first = TexturePaintMath.ConsumeStrokeCoverage(1f, ref coverage);
            float repeated = TexturePaintMath.ConsumeStrokeCoverage(1f, ref coverage);

            Assert.That(first, Is.EqualTo(1f));
            Assert.That(repeated, Is.EqualTo(0f));
            Assert.That(coverage, Is.EqualTo(1f));
        }

        [Test]
        public void StrokeCoverageAccumulatesOnlyTheRemainingPartialAmount()
        {
            float coverage = 0f;

            float first = TexturePaintMath.ConsumeStrokeCoverage(0.4f, ref coverage);
            float second = TexturePaintMath.ConsumeStrokeCoverage(0.4f, ref coverage);
            float final = TexturePaintMath.ConsumeStrokeCoverage(0.4f, ref coverage);

            Assert.That(first, Is.EqualTo(0.4f).Within(0.00001f));
            Assert.That(second, Is.EqualTo(0.4f).Within(0.00001f));
            Assert.That(final, Is.EqualTo(0.2f).Within(0.00001f));
            Assert.That(coverage, Is.EqualTo(1f).Within(0.00001f));
        }

        [Test]
        public void SoftBrushEdgeCannotAccumulatePastItsFalloff()
        {
            float coverage = 0f;

            float first = TexturePaintMath.ConsumeStrokeCoverage(0.15f, 0.25f, ref coverage);
            float second = TexturePaintMath.ConsumeStrokeCoverage(0.15f, 0.25f, ref coverage);
            float repeatedEdge = TexturePaintMath.ConsumeStrokeCoverage(0.15f, 0.25f, ref coverage);
            float closerPass = TexturePaintMath.ConsumeStrokeCoverage(0.5f, 0.75f, ref coverage);

            Assert.That(first, Is.EqualTo(0.15f).Within(0.00001f));
            Assert.That(second, Is.EqualTo(0.1f).Within(0.00001f));
            Assert.That(repeatedEdge, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(closerPass, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(coverage, Is.EqualTo(0.75f).Within(0.00001f));
        }

        [Test]
        public void MeshTriangleMappingUsesTriangleAndBarycentricCoordinate()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 }
            };
            Vector2 uv = MeshReconstructor.TriangleUV(mesh, 0, new Vector3(0.2f, 0.3f, 0.5f));
            Assert.That(uv.x, Is.EqualTo(0.3f).Within(0.00001f));
            Assert.That(uv.y, Is.EqualTo(0.5f).Within(0.00001f));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void DisconnectedUVTrianglesBecomeSeparateIslands()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one, Vector3.one * 2f, Vector3.one * 3f },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, new Vector2(0.6f, 0.6f), new Vector2(0.8f, 0.6f), new Vector2(0.6f, 0.8f) },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            int[] islands = UVIslandUtility.BuildTriangleIslands(mesh);
            Assert.That(islands.Length, Is.EqualTo(2));
            Assert.That(islands[0], Is.Not.EqualTo(islands[1]));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BrushFootprintFindsAdjacentSlotBeforeCenterCrossesBorder()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f),
                    new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.4f, 0f), new Vector2(0.4f, 1f),
                    new Vector2(0.6f, 0f), new Vector2(1f, 0f), new Vector2(0.6f, 1f)
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Brush Footprint Test");
            ReconstructedSurface surface = new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                slotName = "Left",
                slotNames = new List<string> { "Left", "Right" },
                triangleSlotNames = new[] { "Left", "Right" },
                triangleIslands = UVIslandUtility.BuildTriangleIslands(mesh)
            };
            List<SurfaceBrushContact> contacts = new List<SurfaceBrushContact>();

            surface.CollectBrushContacts(new Vector3(0.9f, 0.5f, 0f), 0.2f,
                new[] { "Left", "Right" }, contacts);

            Assert.That(contacts.Exists(contact => contact.slotName == "Left"), Is.True);
            Assert.That(contacts.Exists(contact => contact.slotName == "Right"), Is.True);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BrushFootprintRetainsBothUVBranchesOfOneWrappingIsland()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f),
                    new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0.9f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f),
                    new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0f, 1f)
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Wrapping UV Island Test");
            ReconstructedSurface surface = new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                slotName = "Body",
                slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" },
                triangleIslands = new[] { 0, 0 }
            };
            List<SurfaceBrushContact> contacts = new List<SurfaceBrushContact>();

            surface.CollectBrushContacts(new Vector3(0.9f, 0.5f, 0f), 0.2f,
                new[] { "Body" }, contacts, Vector3.forward, 0f, 180f, true,
                Vector3.right, Vector3.up);

            Assert.That(contacts.Count, Is.EqualTo(2));
            Assert.That(contacts.Exists(contact => contact.brushCenterUV.x > 0.9f), Is.True);
            Assert.That(contacts.Exists(contact => contact.brushCenterUV.x < 0.1f), Is.True);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BrushFootprintRetainsEveryIntersectedTriangleWithinOneUVBranch()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f),
                    Vector3.zero, new Vector3(1f, 1f, 0f), Vector3.up
                },
                uv = new[]
                {
                    Vector2.zero, Vector2.right, Vector2.one,
                    Vector2.zero, Vector2.one, Vector2.up
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Triangle Exact Contact Test");
            ReconstructedSurface surface = new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                slotName = "Body",
                slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" },
                triangleIslands = new[] { 0, 0 }
            };
            List<SurfaceBrushContact> contacts = new List<SurfaceBrushContact>();

            surface.CollectBrushContacts(new Vector3(0.5f, 0.5f, 0f), 0.2f,
                new[] { "Body" }, contacts, Vector3.forward, 0f, 180f, true,
                Vector3.right, Vector3.up);

            Assert.That(contacts.Count, Is.EqualTo(2));
            Assert.That(contacts.Exists(contact => contact.triangleIndex == 0), Is.True);
            Assert.That(contacts.Exists(contact => contact.triangleIndex == 1), Is.True);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BrushProjectionPreservesWorldSizeAcrossDifferentUVDensities()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                    new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f), new Vector3(2f, 1f, 0f)
                },
                uv = new[]
                {
                    Vector2.zero, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f),
                    new Vector2(0.7f, 0.7f), new Vector2(0.95f, 0.7f), new Vector2(0.7f, 0.95f)
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Brush Projection Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };

            BrushProjection first = surface.CalculateBrushProjection(0, 0.2f);
            BrushProjection second = surface.CalculateBrushProjection(1, 0.2f);
            float firstDistance = new Vector2(0.05f * first.uvToBrush.x, 0.05f * first.uvToBrush.z).magnitude;
            float secondDistance = new Vector2(0.025f * second.uvToBrush.x, 0.025f * second.uvToBrush.z).magnitude;

            Assert.That(first.valid && second.valid, Is.True);
            Assert.That(firstDistance, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(secondDistance, Is.EqualTo(0.5f).Within(0.0001f));
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SharedBrushProjectionUsesOneWorldPlaneAcrossTileTriangles()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0.5f), new Vector3(1f, 1f, 0f)
                },
                uv = new[]
                {
                    Vector2.zero, Vector2.right, Vector2.up,
                    Vector2.zero, Vector2.right, Vector2.up
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Shared Brush Projection Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };

            BrushProjection anchor = surface.CalculateBrushProjection(0, 0.2f);
            BrushProjection adjacent = surface.CalculateBrushProjection(1, 0.2f,
                anchor.worldTangent, anchor.worldBitangent);

            Assert.That(anchor.valid && adjacent.valid, Is.True);
            Assert.That(adjacent.worldTangent, Is.EqualTo(anchor.worldTangent));
            Assert.That(adjacent.worldBitangent, Is.EqualTo(anchor.worldBitangent));
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SharedProjectionOriginMatchesAcrossBentTrianglesWithDifferentUVDensity()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0.5f), new Vector3(0f, 1f, 0f)
                },
                uv = new[]
                {
                    Vector2.zero, Vector2.right, Vector2.up,
                    Vector2.zero, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f)
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Shared Brush Origin Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };
            const float radius = 0.25f;
            Vector3 worldCenter = new Vector3(0.55f, 0.45f, 0.2f);
            Vector3 seamWorld = new Vector3(0.5f, 0.5f, 0f);
            Vector2 firstSeamUV = new Vector2(0.5f, 0.5f);
            Vector2 secondSeamUV = new Vector2(0f, 0.25f);
            BrushProjection anchor = surface.CalculateBrushProjection(0, radius);
            BrushProjection first = surface.CalculateBrushProjection(0, radius,
                anchor.worldTangent, anchor.worldBitangent);
            BrushProjection second = surface.CalculateBrushProjection(1, radius,
                anchor.worldTangent, anchor.worldBitangent);

            Assert.That(surface.TryProjectWorldPointToUV(0, worldCenter, anchor.worldTangent,
                anchor.worldBitangent, out Vector2 firstCenterUV), Is.True);
            Assert.That(surface.TryProjectWorldPointToUV(1, worldCenter, anchor.worldTangent,
                anchor.worldBitangent, out Vector2 secondCenterUV), Is.True);
            Vector2 firstBrushPoint = TransformUV(firstSeamUV - firstCenterUV, first.uvToBrush);
            Vector2 secondBrushPoint = TransformUV(secondSeamUV - secondCenterUV, second.uvToBrush);
            Vector3 seamDelta = seamWorld - worldCenter;
            Vector2 expected = new Vector2(Vector3.Dot(seamDelta, anchor.worldTangent),
                Vector3.Dot(seamDelta, anchor.worldBitangent)) / radius;

            Assert.That(firstBrushPoint.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(firstBrushPoint.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(secondBrushPoint.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(secondBrushPoint.y, Is.EqualTo(expected.y).Within(0.0001f));
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void GeometryMaskConservativelyCoversTexelsTouchedByUVBoundary()
        {
            byte[] pixels = new byte[16];

            TexturePaintGeometryMask.RasterizeTriangle(pixels, 4, 4,
                Vector2.zero, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f));

            Assert.That(pixels[2], Is.EqualTo(byte.MaxValue),
                "The first texel beyond a UV edge must be writable when its footprint touches that edge.");
            Assert.That(pixels[3], Is.EqualTo(0), "Conservative coverage must remain limited to the edge texel.");
        }

        [Test]
        public void TriangleRestrictedProjectionClipsPixelsToItsOwnUVTriangle()
        {
            Assert.That(PaintingEngine.PointInsideTriangle(new Vector2(0.2f, 0.2f),
                Vector2.zero, Vector2.right, Vector2.up), Is.True);
            Assert.That(PaintingEngine.PointInsideTriangle(new Vector2(0.8f, 0.8f),
                Vector2.zero, Vector2.right, Vector2.up), Is.False);
            Assert.That(PaintingEngine.PointInsideTriangle(new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.right, Vector2.up), Is.True,
                "A shared edge must remain covered so adjacent triangle dispatches cannot leave a crack.");
        }

        [Test]
        public void ConservativeTriangleClipClosesSubTexelGapBetweenAdjacentTriangles()
        {
            Vector2 halfTexel = Vector2.one * (0.5f / 1024f);
            Vector2 gapPixel = new Vector2(0.5002f, 0.25f);

            bool coveredByLeft = PaintingEngine.PointInsideTriangle(gapPixel,
                Vector2.zero, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), halfTexel);
            bool coveredByRight = PaintingEngine.PointInsideTriangle(gapPixel,
                new Vector2(0.5004f, 0f), Vector2.right, new Vector2(0.5004f, 1f), halfTexel);

            Assert.That(coveredByLeft || coveredByRight, Is.True,
                "A gap narrower than one texel must be owned by at least one adjacent triangle dispatch.");
        }

        [Test]
        public void TrueUvBoundaryCoversACompleteExteriorTexelRing()
        {
            const int resolution = 2048;
            Vector2 boundaryPadding = Vector2.one *
                (PaintingEngine.TriangleBoundaryPaddingTexels / resolution);
            Vector2 exteriorPixel = new Vector2(0.5f + 1.25f / resolution, 0.25f);
            Vector2 a = Vector2.zero;
            Vector2 b = new Vector2(0.5f, 0f);
            Vector2 c = new Vector2(0.5f, 1f);

            Assert.That(PaintingEngine.PointInsideTriangle(exteriorPixel, a, b, c,
                boundaryPadding, 2), Is.True,
                "A real UV boundary must paint the first exterior texel ring used by bilinear filtering.");
            Assert.That(PaintingEngine.PointInsideTriangle(exteriorPixel, a, b, c,
                boundaryPadding, 0), Is.False,
                "A shared triangle edge must not receive UV-boundary dilation.");
        }

        [Test]
        public void SharedEdgeTexelBelongsToExactlyOneTriangle()
        {
            Vector2 point = new Vector2(0.5f, 0.5f);
            bool first = PaintingEngine.PointInsideTriangle(point,
                Vector2.zero, Vector2.right, Vector2.up, default, 5);
            bool second = PaintingEngine.PointInsideTriangle(point,
                Vector2.right, Vector2.one, Vector2.up, default, 3);

            Assert.That(first ^ second, Is.True,
                "A shared edge must be covered once, not accumulated by both triangle dispatches.");
        }

        [Test]
        public void ProjectionPadsOuterEdgesButNotSharedMeshEdge()
        {
            Mesh mesh = CreateSharedEdgeQuad(false);
            GameObject owner = new GameObject("Triangle Edge Ownership Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };

            Assert.That(surface.CalculateBrushProjection(0, 0.2f).triangleBoundaryMask, Is.EqualTo(5));
            Assert.That(surface.CalculateBrushProjection(1, 0.2f).triangleBoundaryMask, Is.EqualTo(3));
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void DuplicatedHardEdgeVerticesStillSharePaintOwnership()
        {
            Mesh mesh = CreateSharedEdgeQuad(true);
            GameObject owner = new GameObject("Duplicated Vertex Edge Ownership Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };

            Assert.That(surface.GetTriangleBoundaryMask(0), Is.EqualTo(5));
            Assert.That(surface.GetTriangleBoundaryMask(1), Is.EqualTo(3));
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void EdgeBetweenDifferentSlotsRetainsConservativePadding()
        {
            Mesh mesh = CreateSharedEdgeQuad(false);
            GameObject owner = new GameObject("Slot Boundary Padding Test");
            ReconstructedSurface surface = new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                triangleSlotNames = new[] { "Tile 1001", "Tile 1002" }
            };

            Assert.That(surface.GetTriangleBoundaryMask(0), Is.EqualTo(7));
            Assert.That(surface.GetTriangleBoundaryMask(1), Is.EqualTo(7));
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ClosestSurfaceProjectionUsesWorldPositionAndNormalSideInsteadOfOverlappingUVs()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 0f, 0.1f), new Vector3(0f, 1f, 0.1f), new Vector3(1f, 0f, 0.1f)
                },
                uv = new[]
                {
                    Vector2.zero, Vector2.right, Vector2.up,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(1f, 0.5f)
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("World Surface Path Projection Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };

            bool found = surface.TryClosestSurfacePoint(new Vector3(0.2f, 0.2f, 0.05f), Vector3.back, 0,
                out Vector3 world, out Vector3 normal, out Vector2 uv, out int triangle, out _);

            Assert.That(found, Is.True);
            Assert.That(triangle, Is.EqualTo(1), "The normal hint must prevent a jump to the opposite surface.");
            Assert.That(world.z, Is.EqualTo(0.1f).Within(0.00001f));
            Assert.That(Vector3.Dot(normal, Vector3.back), Is.GreaterThan(0.99f));
            Assert.That(uv.x, Is.GreaterThanOrEqualTo(0.5f),
                "The UV must be derived from the selected world-space triangle.");
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ClosestSurfaceProjectionCanRestrictCandidatesToSelectedUdimSlots()
        {
            Mesh mesh = CreateSharedEdgeQuad(true);
            GameObject owner = new GameObject("Selected UDIM Projection Test");
            ReconstructedSurface surface = new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                triangleSlotNames = new[] { "Body 1001", "Body 1002" }
            };

            bool found = surface.TryClosestSurfacePoint(new Vector3(0.75f, 0.75f, 0.1f),
                Vector3.forward, -1, new[] { "Body 1001" }, out _, out _, out _,
                out int triangle, out _);

            Assert.That(found, Is.True);
            Assert.That(triangle, Is.EqualTo(0),
                "World projection must not resolve onto a slot outside the selected logical target.");
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void NormalDirectedProjectionDoesNotDeflectAtDuplicatedUvBoundary()
        {
            Mesh mesh = CreateSharedEdgeQuad(true);
            GameObject owner = new GameObject("Normal Directed UV Seam Projection Test");
            MeshCollider collider = owner.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            ReconstructedSurface surface = new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                collider = collider
            };

            Vector3 beforeQuery = new Vector3(0.49f, 0.5f, 0.1f);
            Vector3 afterQuery = new Vector3(0.51f, 0.5f, 0.1f);
            Assert.That(surface.TryProjectAlongNormal(beforeQuery, Vector3.forward, null,
                out Vector3 before, out _, out _, out _, out _), Is.True);
            Assert.That(surface.TryProjectAlongNormal(afterQuery, Vector3.forward, null,
                out Vector3 after, out _, out _, out _, out _), Is.True);
            Assert.That(before.x, Is.EqualTo(beforeQuery.x).Within(0.000001f));
            Assert.That(after.x, Is.EqualTo(afterQuery.x).Within(0.000001f));
            Assert.That(after.x - before.x, Is.EqualTo(0.02f).Within(0.000001f),
                "Crossing a duplicated UV edge must preserve the world-space curve direction.");

            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ConnectedSurfaceProjectionStaysOnAuthoredPolygonStrip()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    // Lower strip. Its second triangle duplicates the shared edge vertices to
                    // model a UV or hard-normal seam.
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
                    // Nearby but topologically separate upper strip.
                    new Vector3(0f, 0f, 0.1f), new Vector3(1f, 0f, 0.1f), new Vector3(0f, 1f, 0.1f),
                    new Vector3(1f, 0f, 0.1f), new Vector3(1f, 1f, 0.1f), new Vector3(0f, 1f, 0.1f)
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }
            };
            mesh.uv = new Vector2[mesh.vertexCount];
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Connected Polygon Strip Projection Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };
            Vector3 query = new Vector3(0.75f, 0.75f, 0.09f);

            Assert.That(surface.AreTrianglesTopologyConnected(0, 1), Is.True,
                "Duplicated seam vertices with the same geometric edge must remain connected.");
            Assert.That(surface.AreTrianglesTopologyConnected(0, 2), Is.False,
                "Nearby overlapping layers must remain separate topology components.");
            Assert.That(surface.TryClosestSurfacePoint(query, Vector3.forward, 0,
                out Vector3 unconstrained, out _, out _, out int unconstrainedTriangle, out _), Is.True);
            Assert.That(unconstrainedTriangle, Is.GreaterThanOrEqualTo(2));
            Assert.That(unconstrained.z, Is.EqualTo(0.1f).Within(0.00001f));

            Assert.That(surface.TryClosestConnectedSurfacePoint(query, Vector3.forward, 0, null,
                out Vector3 connected, out _, out _, out int connectedTriangle, out _), Is.True);
            Assert.That(connectedTriangle, Is.LessThan(2));
            Assert.That(connected.z, Is.EqualTo(0f).Within(0.00001f),
                "A spline anchored to the lower strip must not fall onto the closer upper layer.");

            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SingularGlobalProjectionDoesNotFallBackToTriangleLocalStamp()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.right },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject("Grazing Global Stamp Test");
            ReconstructedSurface surface = new ReconstructedSurface { gameObject = owner, mesh = mesh };
            var contacts = new System.Collections.Generic.List<SurfaceBrushContact>();

            surface.CollectBrushContacts(new Vector3(0f, 0.25f, 0.25f), 0.5f, null, contacts,
                Vector3.right, 1f, 180f, true, Vector3.right, Vector3.up);

            Assert.That(contacts, Is.Empty,
                "A grazing triangle that cannot invert the global projector must be skipped, not locally stamped.");
            Assert.That(ReconstructedSurface.TryProjectionDeterminant(1f, 0f, 0f, 1f, out _), Is.True);
            Assert.That(ReconstructedSurface.TryProjectionDeterminant(1f, 0f, 0f, 0.001f, out _), Is.False);

            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SelectedTargetRaycastIgnoresCloserUnselectedSurfaceAndSupportsBackfaces()
        {
            var reconstruction = new MeshReconstructionResult();
            ReconstructedSurface selected = CreateRaycastSurface("Selected", 0f);
            ReconstructedSurface obstruction = CreateRaycastSurface("Obstruction", 1f);
            reconstruction.surfaces.Add(obstruction);
            reconstruction.surfaces.Add(selected);
            try
            {
                Physics.SyncTransforms();
                bool hitSelected = reconstruction.Raycast(
                    new Ray(new Vector3(0.25f, 0.25f, 2f), Vector3.back),
                    new[] { "Selected" }, false, out ReconstructedSurface frontSurface, out _);
                Assert.That(hitSelected, Is.True);
                Assert.That(frontSurface, Is.SameAs(selected));

                bool rejectedBackface = reconstruction.Raycast(
                    new Ray(new Vector3(0.25f, 0.25f, -1f), Vector3.forward),
                    new[] { "Selected" }, false, out _, out _);
                bool acceptedBackface = reconstruction.Raycast(
                    new Ray(new Vector3(0.25f, 0.25f, -1f), Vector3.forward),
                    new[] { "Selected" }, true, out ReconstructedSurface backSurface, out _);
                Assert.That(rejectedBackface, Is.False);
                Assert.That(acceptedBackface, Is.True);
                Assert.That(backSurface, Is.SameAs(selected));
            }
            finally
            {
                reconstruction.Dispose();
            }
        }

        [Test]
        public void ReconstructionWarningsResolveToLogicalTargetsAndSurfaces()
        {
            var reconstruction = new MeshReconstructionResult();
            var torso = new ReconstructedSurface
            {
                index = 7,
                slotName = "Torso",
                slotNames = new List<string> { "Torso" }
            };
            var legs = new ReconstructedSurface
            {
                index = 9,
                slotName = "Legs",
                slotNames = new List<string> { "Legs" }
            };
            reconstruction.surfaces.Add(torso);
            reconstruction.surfaces.Add(legs);
            reconstruction.logicalTargets.Rebuild(reconstruction.surfaces);
            reconstruction.AddWarning("OP_IMPORT_TEST", MeshReconstructionWarningSeverity.Warning,
                "A recoverable material source condition was found.", new[] { "Torso" }, "Skin");

            reconstruction.ResolveWarningScopes();

            Assert.That(reconstruction.warnings, Has.Count.EqualTo(1),
                "The message-only compatibility view should remain populated.");
            Assert.That(reconstruction.importWarnings, Has.Count.EqualTo(1));
            MeshReconstructionWarning warning = reconstruction.importWarnings[0];
            Assert.That(warning.Code, Is.EqualTo("OP_IMPORT_TEST"));
            Assert.That(warning.TargetIds, Is.EquivalentTo(new[] { "slot:Torso" }));
            Assert.That(warning.SurfaceIndices, Is.EquivalentTo(new[] { 7 }));
            Assert.That(warning.AppliesToTarget(reconstruction.logicalTargets.FindBySlot("Torso")), Is.True);
            Assert.That(warning.AppliesToSurface(torso), Is.True);
            Assert.That(warning.AppliesToSurface(legs), Is.False);

            reconstruction.Dispose();
        }

        private static ReconstructedSurface CreateRaycastSurface(string slot, float z)
        {
            Mesh mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject owner = new GameObject(slot + " Raycast Surface");
            owner.transform.position = new Vector3(0f, 0f, z);
            MeshCollider collider = owner.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                collider = collider,
                slotName = slot,
                slotNames = new List<string> { slot },
                triangleSlotNames = new[] { slot }
            };
        }

        private static Mesh CreateSharedEdgeQuad(bool duplicateSharedVertices)
        {
            Mesh mesh = new Mesh();
            if (duplicateSharedVertices)
            {
                mesh.vertices = new[]
                {
                    Vector3.zero, Vector3.right, Vector3.up,
                    Vector3.right, new Vector3(1f, 1f, 0f), Vector3.up
                };
                mesh.uv = new[]
                {
                    Vector2.zero, Vector2.right, Vector2.up,
                    Vector2.right, Vector2.one, Vector2.up
                };
                mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            }
            else
            {
                mesh.vertices = new[]
                {
                    Vector3.zero, Vector3.right, Vector3.up, new Vector3(1f, 1f, 0f)
                };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2 TransformUV(Vector2 delta, Vector4 uvToBrush)
        {
            return new Vector2(delta.x * uvToBrush.x + delta.y * uvToBrush.y,
                delta.x * uvToBrush.z + delta.y * uvToBrush.w);
        }
    }
}
#endif
