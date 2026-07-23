#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public class UMAClothingConformerTests
    {
        [Test]
        public void MappingRoundTripPreservesSurfaceNormalOffsetAfterBodyDelta()
        {
            Vector3 a = new Vector3(0f, 0f, 0f);
            Vector3 b = new Vector3(1f, 0f, 0f);
            Vector3 c = new Vector3(0f, 1f, 0f);
            Vector3 clothing = new Vector3(0.2f, 0.3f, 0.05f);
            Vector3 closest = ClothingConformerMeshUtility.ClosestPointOnTriangle(clothing, a, b, c);
            Vector3 barycentric = ClothingConformerMeshUtility.CalculateBarycentric(closest, a, b, c);
            float signedOffset = Vector3.Dot(clothing - closest, Vector3.forward);

            // A known blendshape delta moves every body vertex forward by 0.1 m.
            Vector3 delta = Vector3.forward * 0.1f;
            Vector3 currentSurface = barycentric.x * (a + delta) + barycentric.y * (b + delta) + barycentric.z * (c + delta);
            Vector3 conformed = currentSurface + signedOffset * Vector3.forward;

            Assert.That(conformed.x, Is.EqualTo(clothing.x).Within(0.00001f));
            Assert.That(conformed.y, Is.EqualTo(clothing.y).Within(0.00001f));
            Assert.That(conformed.z, Is.EqualTo(0.15f).Within(0.00001f));
        }

        [Test]
        public void ClosestPointReturnsTriangleEdgeForOutsidePoint()
        {
            Vector3 point = ClothingConformerMeshUtility.ClosestPointOnTriangle(
                new Vector3(1.5f, 0.5f, 0f), Vector3.zero, Vector3.right, Vector3.up);

            Assert.That(point.x + point.y, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(point.z, Is.EqualTo(0f).Within(0.00001f));
        }

        [Test]
        public void WeldGroupsJoinUvSplitCopiesButNotConnectedMeshEdges()
        {
            Vector3[] vertices =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0.00001f, 0f, 0f),
                new Vector3(-1f, 0f, 0f),
                new Vector3(0f, -1f, 0f)
            };
            int[] triangles = { 0, 1, 2, 3, 4, 5 };

            int[] groups = ClothingConformerMeshUtility.BuildWeldedVertexGroups(vertices, triangles, 0.0001f);

            Assert.That(groups[0], Is.GreaterThanOrEqualTo(0));
            Assert.That(groups[3], Is.EqualTo(groups[0]));
            Assert.That(groups[1], Is.EqualTo(-1));
        }

        [Test]
        public void CollisionSideFollowsTheOriginalClothingSideOfAnInwardNormal()
        {
            Vector3 inwardSurfaceNormal = Vector3.back;
            Vector3 clothingNormal = Vector3.forward;

            float side = ClothingConformerMeshUtility.GetMappedClothingSide(-0.01f, clothingNormal, inwardSurfaceNormal);
            Vector3 outwardDirection = inwardSurfaceNormal * side;

            Assert.That(side, Is.EqualTo(-1f));
            Assert.That(Vector3.Dot(outwardDirection, clothingNormal), Is.GreaterThan(0.999f));
        }
    }
}
#endif
