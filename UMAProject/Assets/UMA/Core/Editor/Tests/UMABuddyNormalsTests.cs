#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The battle buddy rule for normals: vertices at one position (UV seams, slot connections) are
    /// treated as one vertex and end with one normal, whenever normals are worked out from the shape.
    /// </summary>
    public sealed class UMABuddyNormalsTests
    {
        private static readonly Vector3 P0 = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 P1 = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 LeftTip = new Vector3(-1f, 0.5f, 0f);
        private static readonly Vector3 LeftFace = new Vector3(0f, 0f, 1f);

        // Two triangles along the edge P0-P1 whose edge vertices are duplicated (a UV seam, or the
        // connection between two slots), so the triangles share no vertex index: 0,1,2 and 3,5,4.
        private static readonly int[] FoldTriangles = { 0, 1, 2, 3, 5, 4 };

        private static Vector3 RightTip(float foldDegrees)
        {
            float fold = foldDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(fold), 0.5f, Mathf.Sin(fold));
        }

        private static Vector3[] Fold(float foldDegrees)
        {
            return new[] { P0, P1, LeftTip, P0, P1, RightTip(foldDegrees) };
        }

        private static Vector3 RightFace(float foldDegrees)
        {
            return Vector3.Cross(RightTip(foldDegrees) - P0, P1 - P0).normalized;
        }

        private static Vector3[] Filled(Vector3 value)
        {
            return new[] { value, value, value, value, value, value };
        }

        [TestCase(30f)]
        [TestCase(100f)]
        [TestCase(150f)]
        public void BuddiesShareOneNormalHoweverSharpTheFold(float foldDegrees)
        {
            Vector3[] positions = Fold(foldDegrees);
            var normals = new Vector3[6];
            int written = new UMABuddyNormals(positions, FoldTriangles).Recalculate(positions, normals);

            Assert.That(written, Is.EqualTo(6));
            Assert.That(Vector3.Angle(LeftFace, RightFace(foldDegrees)), Is.EqualTo(foldDegrees).Within(0.01f));
            Assert.That(normals[0], Is.EqualTo(normals[3]), "Buddies have exactly one normal.");
            Assert.That(normals[1], Is.EqualTo(normals[4]));
            Assert.That(Vector3.Angle(normals[0], LeftFace), Is.EqualTo(foldDegrees * 0.5f).Within(0.01f));
            Assert.That(Vector3.Angle(normals[2], LeftFace), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(normals[5], RightFace(foldDegrees)), Is.LessThan(0.01f));
        }

        [Test]
        public void ALockedBuddyKeepsItsNormalAndTheOtherTakesIt()
        {
            Vector3[] positions = Fold(30f);
            var normals = new Vector3[6];
            normals[3] = Vector3.up;
            normals[4] = Vector3.right;
            bool[] locked = { false, false, false, true, true, true };
            new UMABuddyNormals(positions, FoldTriangles).Recalculate(positions, normals, false, locked);

            Assert.That(normals[3], Is.EqualTo(Vector3.up));
            Assert.That(normals[4], Is.EqualTo(Vector3.right));
            Assert.That(normals[5], Is.EqualTo(Vector3.zero), "A locked vertex is not written.");
            Assert.That(normals[0], Is.EqualTo(Vector3.up), "The connection still has one normal.");
            Assert.That(normals[1], Is.EqualTo(Vector3.right));
            Assert.That(Vector3.Angle(normals[2], LeftFace), Is.LessThan(0.01f));
        }

        [Test]
        public void TheTwoSidesOfDoubleSidedGeometryKeepTheirOwnNormals()
        {
            // The same triangle twice, the second one reversed: its buddies face the other way.
            Vector3[] positions = { P0, P1, LeftTip, P0, P1, LeftTip };
            int[] triangles = { 0, 1, 2, 3, 5, 4 };
            var normals = new Vector3[6];
            new UMABuddyNormals(positions, triangles).Recalculate(positions, normals);

            for (int i = 0; i < 3; i++)
            {
                Assert.That(Vector3.Angle(normals[i], LeftFace), Is.LessThan(0.01f));
                Assert.That(Vector3.Angle(normals[i + 3], -LeftFace), Is.LessThan(0.01f));
            }
        }

        [Test]
        public void FollowTurnsNormalsWithTheirFacesAndLeavesTheRest()
        {
            Vector3[] before = Fold(0f);
            Vector3[] after = Fold(40f);
            // Normals that are not the faces' own: tilted 10 degrees, as a slot's own normals can be.
            Vector3 custom = Quaternion.AngleAxis(10f, Vector3.right) * LeftFace;
            Vector3[] beforeNormals = Filled(custom);
            var normals = (Vector3[])beforeNormals.Clone();
            int written = new UMABuddyNormals(before, FoldTriangles).Follow(before, after, beforeNormals, normals);

            Assert.That(written, Is.EqualTo(5));
            Assert.That(normals[2], Is.EqualTo(custom), "No face around the left tip moved.");
            Assert.That(normals[0], Is.EqualTo(normals[3]), "Buddies still have exactly one normal.");
            Assert.That(normals[1], Is.EqualTo(normals[4]));
            // The faces around the seam turned 20 degrees on average, the right tip's own face 40.
            Assert.That(Vector3.Angle(normals[0], custom), Is.EqualTo(20f).Within(0.5f));
            Assert.That(Vector3.Angle(normals[5], custom), Is.EqualTo(40f).Within(1f));
            Vector3 seamFaces = (LeftFace + RightFace(40f)).normalized;
            Assert.That(Vector3.Angle(normals[0], seamFaces), Is.EqualTo(10f).Within(0.01f), "The normal keeps its tilt from the faces.");
        }

        [Test]
        public void FollowGivesBuddiesThatDisagreedOneNormal()
        {
            Vector3[] before = Fold(20f);
            Vector3[] after = Fold(30f);
            // What Mesh.RecalculateNormals leaves at a seam: each copy has the normal of its own side.
            Vector3[] beforeNormals = { LeftFace, LeftFace, LeftFace, RightFace(20f), RightFace(20f), RightFace(20f) };
            var normals = (Vector3[])beforeNormals.Clone();
            new UMABuddyNormals(before, FoldTriangles).Follow(before, after, beforeNormals, normals);

            Assert.That(normals[0], Is.EqualTo(normals[3]));
            Assert.That(normals[1], Is.EqualTo(normals[4]));
            Assert.That(Vector3.Angle(normals[0], (LeftFace + RightFace(30f)).normalized), Is.LessThan(0.05f));
        }

        [Test]
        public void FollowWritesNothingWhenNothingMoved()
        {
            Vector3[] positions = Fold(30f);
            Vector3[] beforeNormals = { LeftFace, LeftFace, LeftFace, RightFace(30f), RightFace(30f), RightFace(30f) };
            var normals = (Vector3[])beforeNormals.Clone();
            int written = new UMABuddyNormals(positions, FoldTriangles).Follow(positions, (Vector3[])positions.Clone(), beforeNormals, normals);

            Assert.That(written, Is.EqualTo(0));
            Assert.That(normals, Is.EqualTo(beforeNormals));
        }

        [Test]
        public void FollowGivesALockedBuddysNormalToTheOneThatMoved()
        {
            Vector3[] before = Fold(20f);
            Vector3[] after = Fold(60f);
            Vector3[] beforeNormals = { LeftFace, LeftFace, LeftFace, RightFace(20f), RightFace(20f), RightFace(20f) };
            var normals = (Vector3[])beforeNormals.Clone();
            // The left triangle belongs to a slot that is not being edited.
            bool[] locked = { true, true, true, false, false, false };
            new UMABuddyNormals(before, FoldTriangles).Follow(before, after, beforeNormals, normals, locked);

            Assert.That(normals[0], Is.EqualTo(LeftFace));
            Assert.That(normals[3], Is.EqualTo(LeftFace), "The connection has the locked slot's normal.");
            Assert.That(normals[4], Is.EqualTo(LeftFace));
            Assert.That(Vector3.Angle(normals[5], RightFace(60f)), Is.LessThan(0.05f));
        }
    }
}
#endif
