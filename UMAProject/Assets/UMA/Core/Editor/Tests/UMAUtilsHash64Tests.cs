using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public class UMAUtilsHash64Tests
    {
        [Test]
        public void Hash64MatchesKnownFnv1aValue()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("hello");

            ulong hash = UMAUtils.Hash64(bytes);

            Assert.AreEqual(0xA430D84680AABD0BUL, hash);
        }

        [Test]
        public void Hash64CanBeCalculatedIncrementally()
        {
            byte[] first = Encoding.UTF8.GetBytes("geometry-");
            byte[] second = Encoding.UTF8.GetBytes("data");
            byte[] combined = Encoding.UTF8.GetBytes("geometry-data");

            ulong incremental = UMAUtils.Hash64(first);
            incremental = UMAUtils.Hash64(second, incremental);

            Assert.AreEqual(UMAUtils.Hash64(combined), incremental);
        }

        [Test]
        public void MeshHashChangesWithVerticesAndTriangles()
        {
            var meshData = new UMAMeshData
            {
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                submeshes = new[]
                {
                    new SubMeshTriangles(new[] { 0, 1, 2 })
                }
            };

            ulong original = meshData.CalculateHashCode();
            Assert.AreEqual(original, meshData.CalculateHashCode());

            meshData.vertices[1] = new Vector3(2f, 0f, 0f);
            ulong changedVertex = meshData.CalculateHashCode();
            Assert.AreNotEqual(original, changedVertex);

            meshData.vertices[1] = Vector3.right;
            meshData.submeshes[0].SetTriangles(new[] { 0, 2, 1 });
            ulong changedTriangles = meshData.CalculateHashCode();
            Assert.AreNotEqual(original, changedTriangles);
        }
    }
}
