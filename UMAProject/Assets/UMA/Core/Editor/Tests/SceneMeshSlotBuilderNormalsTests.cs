#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Scene Mesh Slot Builder works out normals for a source mesh that has none. Battle buddies
    /// (vertices at one position) must end with one shared normal, as everywhere else in UMA.
    /// </summary>
    public sealed class SceneMeshSlotBuilderNormalsTests
    {
        // Two triangles folded by 90 degrees along the edge P0-P1. The edge vertices are duplicated,
        // as along a UV island edge, so the triangles share no vertex index.
        private static readonly Vector3[] Fold =
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(-1f, 0.5f, 0f),
            new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0.5f, 1f)
        };

        private static readonly int[] FoldTriangles = { 0, 1, 2, 3, 5, 4 };

        [Test]
        public void MissingNormals_GiveBattleBuddiesOneNormal()
        {
            Vector3[] normals = UMASlotSkinTransfer.CalculateMissingNormals(Fold, Fold, FoldTriangles);

            Assert.AreEqual(Fold.Length, normals.Length);
            Assert.Less(Vector3.Angle(normals[0], normals[3]), 0.01f, "The copies at P0 must share a normal.");
            Assert.Less(Vector3.Angle(normals[1], normals[4]), 0.01f, "The copies at P1 must share a normal.");
            for (int i = 0; i < normals.Length; i++)
            {
                Assert.AreEqual(1f, normals[i].magnitude, 0.0001f, "Normal " + i + " must be unit length.");
            }
            // The shared normal lies between the two faces; the tips keep their own face's normal.
            Assert.AreEqual(45f, Vector3.Angle(normals[0], normals[2]), 0.5f);
            Assert.AreEqual(45f, Vector3.Angle(normals[0], normals[5]), 0.5f);
            Assert.AreEqual(90f, Vector3.Angle(normals[2], normals[5]), 0.5f);
        }

        [Test]
        public void MissingNormals_FindBuddiesFromTheSourcePositions()
        {
            // In bind space the two copies of the edge have drifted apart a little (unskinning by
            // slightly different weights). They are still one point of the source surface.
            var bind = (Vector3[])Fold.Clone();
            bind[3] += new Vector3(0f, 0f, 0.001f);
            bind[4] += new Vector3(0f, 0f, 0.001f);

            Vector3[] normals = UMASlotSkinTransfer.CalculateMissingNormals(Fold, bind, FoldTriangles);

            Assert.Less(Vector3.Angle(normals[0], normals[3]), 0.01f);
            Assert.Less(Vector3.Angle(normals[1], normals[4]), 0.01f);
        }

        [Test]
        public void UnityRecalculateNormals_SplitsTheSameBuddies()
        {
            // The reason the builder does not use Mesh.RecalculateNormals: it gives each copy the
            // normal of its own face, so the seam shows.
            var mesh = new Mesh { vertices = Fold, triangles = FoldTriangles };
            try
            {
                mesh.RecalculateNormals();
                Vector3[] normals = mesh.normals;
                Assert.Greater(Vector3.Angle(normals[0], normals[3]), 80f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif
