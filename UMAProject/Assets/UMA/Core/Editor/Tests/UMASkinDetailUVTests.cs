#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public class UMASkinDetailUVTests
    {
        const float Target = UMASkinDetailUVUtility.DefaultSurfacePerUV;

        // A flat square with its own four vertices, mapped to a UV rectangle.
        static void AddQuad(List<Vector3> vertices, List<Vector2> uv, List<int> triangles, Vector3 origin, float size, Vector2 uvOrigin, Vector2 uvSize)
        {
            int first = vertices.Count;
            vertices.Add(origin); uv.Add(uvOrigin);
            vertices.Add(origin + new Vector3(size, 0, 0)); uv.Add(uvOrigin + new Vector2(uvSize.x, 0));
            vertices.Add(origin + new Vector3(size, size, 0)); uv.Add(uvOrigin + uvSize);
            vertices.Add(origin + new Vector3(0, size, 0)); uv.Add(uvOrigin + new Vector2(0, uvSize.y));
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }

        // Surface covered by one UV unit, over a range of an index buffer.
        static float SurfacePerUV(Vector3[] vertices, Vector2[] uv, int[] triangles, int start, int length)
        {
            double surface = 0, uvArea = 0;
            for (int t = start; t < start + length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                surface += Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5;
                Vector2 e1 = uv[b] - uv[a], e2 = uv[c] - uv[a];
                uvArea += Math.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5;
            }
            return (float)Math.Sqrt(surface / uvArea);
        }

        [Test]
        public void EveryIslandCoversTheRequestedSurfacePerUV()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            // Two islands laid out at very different scales: 2 and 0.5 units of surface per UV unit.
            AddQuad(vertices, uv, triangles, Vector3.zero, 1f, new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.5f));
            AddQuad(vertices, uv, triangles, new Vector3(5, 0, 0), 0.2f, new Vector2(0.55f, 0.5f), new Vector2(0.4f, 0.4f));
            var v = vertices.ToArray(); var t = triangles.ToArray();
            Assert.That(SurfacePerUV(v, uv.ToArray(), t, 0, 6), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(SurfacePerUV(v, uv.ToArray(), t, 6, 6), Is.EqualTo(0.5f).Within(1e-4f));

            var detail = UMASkinDetailUVUtility.ComputeDetailUVs(v, uv.ToArray(), new[] { t }, Target, out int islands);

            Assert.That(islands, Is.EqualTo(2));
            Assert.That(detail.Length, Is.EqualTo(v.Length));
            Assert.That(SurfacePerUV(v, detail, t, 0, 6), Is.EqualTo(Target).Within(1e-4f));
            Assert.That(SurfacePerUV(v, detail, t, 6, 6), Is.EqualTo(Target).Within(1e-4f));
        }

        [Test]
        public void IslandsKeepTheirShapeAndNoCoordinateIsZero()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            // Starts at the UV origin: the plain scaled copy of this island would contain (0,0).
            AddQuad(vertices, uv, triangles, Vector3.zero, 1f, Vector2.zero, new Vector2(0.25f, 0.5f));
            var source = uv.ToArray();

            var detail = UMASkinDetailUVUtility.ComputeDetailUVs(vertices.ToArray(), source, new[] { triangles.ToArray() }, Target, out _);

            // One scale for the whole island: every edge is the source edge times the same factor.
            float scale = (detail[1] - detail[0]).magnitude / (source[1] - source[0]).magnitude;
            for (int i = 1; i < detail.Length; i++)
            {
                Vector2 expected = (source[i] - source[0]) * scale;
                Assert.That(Vector2.Distance(detail[i] - detail[0], expected), Is.LessThan(1e-5f), "vertex " + i);
            }
            // The shaders read anything below 1 as "no detail coordinates on this mesh".
            foreach (var coordinate in detail)
            {
                Assert.That(coordinate.x, Is.GreaterThanOrEqualTo(1f));
                Assert.That(coordinate.y, Is.GreaterThanOrEqualTo(1f));
            }
        }

        [Test]
        public void VerticesSplitAtOnePositionAndUVStayInOneIsland()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            // Two squares that meet along an edge without sharing vertices, as a mesh split for normals does.
            // The second is stretched in UV, so scaling them separately would pull the shared edge apart.
            AddQuad(vertices, uv, triangles, Vector3.zero, 1f, Vector2.zero, new Vector2(0.2f, 0.2f));
            AddQuad(vertices, uv, triangles, new Vector3(1, 0, 0), 1f, new Vector2(0.2f, 0f), new Vector2(0.6f, 0.2f));

            var detail = UMASkinDetailUVUtility.ComputeDetailUVs(vertices.ToArray(), uv.ToArray(), new[] { triangles.ToArray() }, Target, out int islands);

            Assert.That(islands, Is.EqualTo(1));
            // First square's right edge (vertices 1 and 2) is the second square's left edge (vertices 4 and 7).
            Assert.That(Vector2.Distance(detail[1], detail[4]), Is.LessThan(1e-6f));
            Assert.That(Vector2.Distance(detail[2], detail[7]), Is.LessThan(1e-6f));
        }

        [Test]
        public void LooseVerticesAndCollapsedTrianglesUseTheMeshScale()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            AddQuad(vertices, uv, triangles, Vector3.zero, 1f, new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.5f));
            vertices.Add(new Vector3(9, 9, 9)); uv.Add(new Vector2(0.9f, 0.9f)); // used by no triangle
            int collapsed = vertices.Count;
            for (int i = 0; i < 3; i++) { vertices.Add(new Vector3(3, 3, 3)); uv.Add(new Vector2(0.7f, 0.7f)); }
            triangles.AddRange(new[] { collapsed, collapsed + 1, collapsed + 2 });

            var detail = UMASkinDetailUVUtility.ComputeDetailUVs(vertices.ToArray(), uv.ToArray(), new[] { triangles.ToArray() }, Target, out int islands);

            Assert.That(islands, Is.EqualTo(1));
            foreach (var coordinate in detail)
            {
                Assert.That(float.IsNaN(coordinate.x) || float.IsInfinity(coordinate.x) || float.IsNaN(coordinate.y) || float.IsInfinity(coordinate.y), Is.False);
                Assert.That(coordinate, Is.Not.EqualTo(Vector2.zero));
            }
        }

        [Test]
        public void BakeWritesOnlyTheFourthUVChannelAndClearRemovesIt()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            AddQuad(vertices, uv, triangles, Vector3.zero, 1f, new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.5f));
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                var mesh = new UMAMeshData
                {
                    vertices = vertices.ToArray(),
                    uv = uv.ToArray(),
                    submeshes = new[] { new SubMeshTriangles(triangles.ToArray()) },
                    subMeshCount = 1,
                    vertexCount = vertices.Count
                };
                slot.meshData = mesh;
                var sourceUV = mesh.uv; var sourceVertices = mesh.vertices;
                Assert.That(UMASkinDetailUVUtility.HasDetailUVs(slot), Is.False);

                int islands = UMASkinDetailUVUtility.BakeSlot(slot);

                Assert.That(islands, Is.EqualTo(1));
                Assert.That(UMASkinDetailUVUtility.HasDetailUVs(slot), Is.True);
                Assert.That(mesh.uv, Is.SameAs(sourceUV));
                Assert.That(mesh.vertices, Is.SameAs(sourceVertices));
                Assert.That(mesh.uv2 == null || mesh.uv2.Length == 0);
                Assert.That(mesh.uv3 == null || mesh.uv3.Length == 0);
                Assert.That(SurfacePerUV(mesh.vertices, mesh.uv4, triangles.ToArray(), 0, 6), Is.EqualTo(Target).Within(1e-4f));

                Assert.That(UMASkinDetailUVUtility.ClearSlot(slot), Is.True);
                Assert.That(UMASkinDetailUVUtility.HasDetailUVs(slot), Is.False);
                Assert.That(UMASkinDetailUVUtility.ClearSlot(slot), Is.False);
            }
            finally { Object.DestroyImmediate(slot); }
        }

        // A flat sheet of squares whose texture layout crowds one side: u runs as the square of the position.
        static void AddUnevenSheet(List<Vector3> vertices, List<Vector2> uv, List<int> triangles, int cells, bool mirrored)
        {
            int first = vertices.Count;
            for (int y = 0; y <= cells; y++)
                for (int x = 0; x <= cells; x++)
                {
                    float fx = x / (float)cells, fy = y / (float)cells;
                    vertices.Add(new Vector3(fx, fy, 0));
                    float u = 0.1f + 0.8f * fx * fx;
                    uv.Add(new Vector2(mirrored ? 1f - u : u, 0.1f + 0.8f * fy));
                }
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    int a = first + y * (cells + 1) + x, b = a + 1, c = a + cells + 2, d = a + cells + 1;
                    triangles.AddRange(new[] { a, b, c, a, c, d });
                }
        }

        // Smallest and largest surface per UV unit over the triangles, and the worst stretch of a triangle
        // (1 = the same size in every direction).
        static void Evenness(Vector3[] vertices, Vector2[] uv, int[] triangles, out float smallest, out float largest, out float worstStretch)
        {
            smallest = float.MaxValue; largest = 0; worstStretch = 1;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                float size = SurfacePerUV(vertices, uv, triangles, t, 3);
                smallest = Mathf.Min(smallest, size); largest = Mathf.Max(largest, size);
                // Stretch from the lengths the three edges get per UV unit; all equal on an undistorted triangle
                // only when compared with the mesh, so compare each edge's ratio.
                float r1 = (vertices[b] - vertices[a]).magnitude / (uv[b] - uv[a]).magnitude;
                float r2 = (vertices[c] - vertices[b]).magnitude / (uv[c] - uv[b]).magnitude;
                float r3 = (vertices[a] - vertices[c]).magnitude / (uv[a] - uv[c]).magnitude;
                worstStretch = Mathf.Max(worstStretch, Mathf.Max(r1, Mathf.Max(r2, r3)) / Mathf.Min(r1, Mathf.Min(r2, r3)));
            }
        }

        [Test]
        public void RelaxedCoordinatesEvenOutAnIslandThatIsUnevenInside()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            AddUnevenSheet(vertices, uv, triangles, 8, false);
            var v = vertices.ToArray(); var t = triangles.ToArray();

            // Scaling the island as a whole keeps the crowding: the detail map would be many times larger on one side.
            var scaled = UMASkinDetailUVUtility.ComputeDetailUVs(v, uv.ToArray(), new[] { t }, Target, out _);
            Evenness(v, scaled, t, out float smallestBefore, out float largestBefore, out _);
            Assert.That(largestBefore / smallestBefore, Is.GreaterThan(3f));

            var relaxed = UMASkinDetailUVUtility.ComputeRelaxedDetailUVs(v, uv.ToArray(), new[] { t }, Target, out int islands);

            Assert.That(islands, Is.EqualTo(1));
            Assert.That(relaxed.Length, Is.EqualTo(v.Length));
            // A flat sheet can be laid out exactly: one size and no stretch everywhere.
            Evenness(v, relaxed, t, out float smallest, out float largest, out float stretch);
            Assert.That(smallest, Is.EqualTo(Target).Within(Target * 0.02f));
            Assert.That(largest, Is.EqualTo(Target).Within(Target * 0.02f));
            Assert.That(stretch, Is.LessThan(1.03f));
            foreach (var coordinate in relaxed)
            {
                Assert.That(coordinate.x, Is.GreaterThanOrEqualTo(1f));
                Assert.That(coordinate.y, Is.GreaterThanOrEqualTo(1f));
            }
        }

        [Test]
        public void RelaxedCoordinatesKeepAMirroredIslandMirrored()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            AddUnevenSheet(vertices, uv, triangles, 6, true);
            var v = vertices.ToArray(); var t = triangles.ToArray(); var source = uv.ToArray();

            var relaxed = UMASkinDetailUVUtility.ComputeRelaxedDetailUVs(v, source, new[] { t }, Target, out _);

            for (int i = 0; i < t.Length; i += 3)
            {
                Vector2 s1 = source[t[i + 1]] - source[t[i]], s2 = source[t[i + 2]] - source[t[i]];
                Vector2 r1 = relaxed[t[i + 1]] - relaxed[t[i]], r2 = relaxed[t[i + 2]] - relaxed[t[i]];
                Assert.That(Mathf.Sign(r1.x * r2.y - r1.y * r2.x), Is.EqualTo(Mathf.Sign(s1.x * s2.y - s1.y * s2.x)), "triangle " + i / 3);
            }
            Evenness(v, relaxed, t, out float smallest, out float largest, out _);
            Assert.That(largest / smallest, Is.LessThan(1.05f));
        }

        [Test]
        public void RelaxedCoordinatesKeepSplitVerticesTogetherAndSurviveBadTriangles()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            // Two squares that meet along an edge without sharing vertices; the second is stretched in UV.
            AddQuad(vertices, uv, triangles, Vector3.zero, 1f, Vector2.zero, new Vector2(0.2f, 0.2f));
            AddQuad(vertices, uv, triangles, new Vector3(1, 0, 0), 1f, new Vector2(0.2f, 0f), new Vector2(0.6f, 0.2f));
            vertices.Add(new Vector3(9, 9, 9)); uv.Add(new Vector2(0.9f, 0.9f)); // used by no triangle
            int collapsed = vertices.Count;
            for (int i = 0; i < 3; i++) { vertices.Add(new Vector3(3, 3, 3)); uv.Add(new Vector2(0.7f, 0.7f)); }
            triangles.AddRange(new[] { collapsed, collapsed + 1, collapsed + 2 });
            var v = vertices.ToArray(); var t = triangles.ToArray();

            var relaxed = UMASkinDetailUVUtility.ComputeRelaxedDetailUVs(v, uv.ToArray(), new[] { t }, Target, out _);

            Assert.That(Vector2.Distance(relaxed[1], relaxed[4]), Is.LessThan(1e-6f));
            Assert.That(Vector2.Distance(relaxed[2], relaxed[7]), Is.LessThan(1e-6f));
            Assert.That(SurfacePerUV(v, relaxed, t, 0, 6), Is.EqualTo(Target).Within(Target * 0.02f));
            Assert.That(SurfacePerUV(v, relaxed, t, 6, 6), Is.EqualTo(Target).Within(Target * 0.02f));
            foreach (var coordinate in relaxed)
            {
                Assert.That(float.IsNaN(coordinate.x) || float.IsInfinity(coordinate.x) || float.IsNaN(coordinate.y) || float.IsInfinity(coordinate.y), Is.False);
                Assert.That(coordinate.x, Is.GreaterThanOrEqualTo(1f));
                Assert.That(coordinate.y, Is.GreaterThanOrEqualTo(1f));
            }
        }

        [Test]
        public void RelaxedBakeWritesOnlyTheFourthUVChannel()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            AddUnevenSheet(vertices, uv, triangles, 4, false);
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                var mesh = new UMAMeshData
                {
                    vertices = vertices.ToArray(),
                    uv = uv.ToArray(),
                    submeshes = new[] { new SubMeshTriangles(triangles.ToArray()) },
                    subMeshCount = 1,
                    vertexCount = vertices.Count
                };
                slot.meshData = mesh;
                var sourceUV = mesh.uv; var sourceVertices = mesh.vertices; var copyOfUV = (Vector2[])mesh.uv.Clone();

                int islands = UMASkinDetailUVUtility.BakeSlotRelaxed(slot);

                Assert.That(islands, Is.EqualTo(1));
                Assert.That(UMASkinDetailUVUtility.HasDetailUVs(slot), Is.True);
                Assert.That(mesh.uv, Is.SameAs(sourceUV));
                Assert.That(mesh.uv, Is.EqualTo(copyOfUV), "the texture layout must not change");
                Assert.That(mesh.vertices, Is.SameAs(sourceVertices));
                Assert.That(mesh.uv2 == null || mesh.uv2.Length == 0);
                Assert.That(mesh.uv3 == null || mesh.uv3.Length == 0);
                Evenness(mesh.vertices, mesh.uv4, triangles.ToArray(), out float smallest, out float largest, out _);
                Assert.That(largest / smallest, Is.LessThan(1.05f));
            }
            finally { Object.DestroyImmediate(slot); }
        }

        // The UMA 3 body is five texture tiles laid out at scales from 0.33 to 1.22 mesh units per UV unit.
        // Without baked detail coordinates, skin pores are up to 3.7 times larger on the body than on the face.
        [TestCase("1001")]
        [TestCase("1002")]
        [TestCase("1003")]
        [TestCase("1004")]
        [TestCase("1005")]
        public void UMA3BodySlotHasEvenDetailCoordinates(string tile)
        {
            string path = "Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM" + tile + "_slot.asset";
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
            if (slot == null) Assert.Ignore("The UMA 3 content is not installed.");
            Assert.That(UMASkinDetailUVUtility.HasDetailUVs(slot), Is.True, path + " has no fourth UV channel. Select the slot and run Assets > UMA > Skin > Bake Even Detail UVs On Selected Slots.");
            var mesh = slot.meshData;
            int[] triangles = mesh.submeshes[0].GetBaseTriangles();
            Assert.That(SurfacePerUV(mesh.vertices, mesh.uv4, triangles, 0, triangles.Length), Is.EqualTo(Target).Within(Target * 0.02f));
            foreach (var coordinate in mesh.uv4)
            {
                Assert.That(coordinate.x, Is.GreaterThanOrEqualTo(1f));
                Assert.That(coordinate.y, Is.GreaterThanOrEqualTo(1f));
            }
        }
    }
}
#endif
