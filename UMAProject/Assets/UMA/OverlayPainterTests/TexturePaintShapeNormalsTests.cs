#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    /// <summary>
    /// A paint surface whose source mesh has no normals gets them from its shape. Vertices at one position (the
    /// copies along a UV seam or where slots connect) must share one normal, which Mesh.RecalculateNormals does not do.
    /// </summary>
    public sealed class TexturePaintShapeNormalsTests
    {
        [Test]
        public void SeamCopiesGetOneSharedNormal()
        {
            // Two triangles folded along the edge (0,0,0)-(0,1,0). The second triangle has its own copies of the
            // two edge vertices, as a UV seam does.
            Vector3[] vertices =
            {
                new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f)
            };
            int[] triangles = { 0, 2, 1, 3, 5, 4 };
            Vector3[] normals = ProceduralMeshMapBuilder.ShapeNormals(vertices, triangles);

            Assert.That(normals, Has.Length.EqualTo(vertices.Length));
            Assert.That(Vector3.Angle(normals[1], normals[3]), Is.LessThan(0.01f), "The copies at (0,0,0) share a normal.");
            Assert.That(Vector3.Angle(normals[2], normals[5]), Is.LessThan(0.01f), "The copies at (0,1,0) share a normal.");
            Assert.That(Vector3.Angle(normals[0], normals[4]), Is.GreaterThan(45f), "The two faces do point different ways.");
            for (int i = 0; i < normals.Length; i++)
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(0.001f), "normal " + i);
        }
    }
}
#endif
