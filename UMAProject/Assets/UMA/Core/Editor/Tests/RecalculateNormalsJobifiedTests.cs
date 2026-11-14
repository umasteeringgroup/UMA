#if UNITY_EDITOR && UMA_BURSTCOMPILE

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class RecalculateNormalsJobifiedTests
    {
        private sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();

            public int Add(Vector3 position, Vector2 uv)
            {
                vertices.Add(position);
                uvs.Add(uv);
                return vertices.Count - 1;
            }

            public void Triangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }

        private static void Run(Geometry geometry, float angle, Vector3[] startNormals, out Vector3[] normals, out Vector4[] tangents)
        {
            int count = geometry.vertices.Count;
            var nativeVertices = new NativeArray<Vector3>(geometry.vertices.ToArray(), Allocator.TempJob);
            var nativeNormals = startNormals != null ? new NativeArray<Vector3>(startNormals, Allocator.TempJob) : new NativeArray<Vector3>(count, Allocator.TempJob);
            var nativeUVs = new NativeArray<Vector2>(geometry.uvs.ToArray(), Allocator.TempJob);
            var nativeTangents = new NativeArray<Vector4>(count, Allocator.TempJob);
            var nativeTriangles = new NativeArray<int>(geometry.triangles.ToArray(), Allocator.TempJob);
            try
            {
                MeshUtilities.RecalculateNormalsTangentsJobified(nativeVertices, nativeNormals, nativeUVs, nativeTangents, nativeTriangles, angle).Complete();
                normals = nativeNormals.ToArray();
                tangents = nativeTangents.ToArray();
            }
            finally
            {
                nativeVertices.Dispose();
                nativeNormals.Dispose();
                nativeUVs.Dispose();
                nativeTangents.Dispose();
                nativeTriangles.Dispose();
            }
        }

        // The algorithm written out plainly. Corners at one position are taken in triangle order; a vertex is
        // worked out from its first triangle; its own triangles always count, those of other vertices at the
        // same position count when their face is within the angle.
        private static Vector3[] ReferenceNormals(Geometry geometry, float angle, Vector3[] startNormals)
        {
            List<Vector3> vertices = geometry.vertices;
            List<int> triangles = geometry.triangles;
            int triangleCount = triangles.Count / 3;
            var faceNormals = new Vector3[triangleCount];
            var cornersAtPosition = new Dictionary<int, List<int>>();
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                Vector3 a = vertices[triangles[triangle * 3]];
                Vector3 b = vertices[triangles[triangle * 3 + 1]];
                Vector3 c = vertices[triangles[triangle * 3 + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                float magnitude = normal.magnitude;
                faceNormals[triangle] = magnitude > 0f ? normal / magnitude : normal;
                for (int corner = 0; corner < 3; corner++)
                {
                    int hash = MeshUtilities.Vector3Hash(vertices[triangles[triangle * 3 + corner]]);
                    if (!cornersAtPosition.TryGetValue(hash, out List<int> list))
                    {
                        list = new List<int>();
                        cornersAtPosition.Add(hash, list);
                    }
                    list.Add(triangle * 3 + corner);
                }
            }

            var normals = startNormals != null ? (Vector3[])startNormals.Clone() : new Vector3[vertices.Count];
            float cosine = Mathf.Cos(angle * Mathf.Deg2Rad);
            var done = new HashSet<int>();
            foreach (List<int> corners in cornersAtPosition.Values)
            {
                done.Clear();
                foreach (int lhs in corners)
                {
                    int vertex = triangles[lhs];
                    if (!done.Add(vertex))
                    {
                        continue;
                    }
                    Vector3 sum = Vector3.zero;
                    foreach (int rhs in corners)
                    {
                        if (triangles[rhs] == vertex || Vector3.Dot(faceNormals[lhs / 3], faceNormals[rhs / 3]) >= cosine)
                        {
                            sum += faceNormals[rhs / 3];
                        }
                    }
                    normals[vertex] = sum.normalized;
                }
            }
            return normals;
        }

        // Tangents from triangles added up one after another, which is the only order with a defined result.
        private static Vector4[] ReferenceTangents(Geometry geometry, Vector3[] normals)
        {
            List<Vector3> vertices = geometry.vertices;
            List<Vector2> uvs = geometry.uvs;
            List<int> triangles = geometry.triangles;
            var tan1 = new Vector3[vertices.Count];
            var tan2 = new Vector3[vertices.Count];
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int i1 = triangles[i], i2 = triangles[i + 1], i3 = triangles[i + 2];
                Vector3 e1 = vertices[i2] - vertices[i1];
                Vector3 e2 = vertices[i3] - vertices[i1];
                float s1 = uvs[i2].x - uvs[i1].x, s2 = uvs[i3].x - uvs[i1].x;
                float t1 = uvs[i2].y - uvs[i1].y, t2 = uvs[i3].y - uvs[i1].y;
                float div = s1 * t2 - s2 * t1;
                float r = div == 0f ? 0f : 1f / div;
                Vector3 sDir = new Vector3((t2 * e1.x - t1 * e2.x) * r, (t2 * e1.y - t1 * e2.y) * r, (t2 * e1.z - t1 * e2.z) * r);
                Vector3 tDir = new Vector3((s1 * e2.x - s2 * e1.x) * r, (s1 * e2.y - s2 * e1.y) * r, (s1 * e2.z - s2 * e1.z) * r);
                tan1[i1] += sDir; tan1[i2] += sDir; tan1[i3] += sDir;
                tan2[i1] += tDir; tan2[i2] += tDir; tan2[i3] += tDir;
            }
            var tangents = new Vector4[vertices.Count];
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 n = normals[i];
                Vector3 t = tan1[i];
                Vector3.OrthoNormalize(ref n, ref t);
                tangents[i] = new Vector4(t.x, t.y, t.z, Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0f ? -1f : 1f);
            }
            return tangents;
        }

        // A sphere with outward-facing triangles. The column where the texture wraps is doubled, so those
        // vertices share a position but not an index, as on any mesh with a UV seam.
        private static Geometry Sphere(int rings, int segments)
        {
            var geometry = new Geometry();
            for (int ring = 0; ring <= rings; ring++)
            {
                float theta = Mathf.PI * ring / rings;
                for (int segment = 0; segment <= segments; segment++)
                {
                    float phi = 2f * Mathf.PI * (segment % segments) / segments;
                    geometry.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)),
                        new Vector2((float)segment / segments, 1f - (float)ring / rings));
                }
            }
            int stride = segments + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int a = ring * stride + segment, b = a + stride, c = b + 1, d = a + 1;
                    // At a pole two corners of the quad coincide; leave out the triangle with no area.
                    if (ring > 0) geometry.Triangle(a, d, b);
                    if (ring < rings - 1) geometry.Triangle(b, d, c);
                }
            }
            return geometry;
        }

        // A sheet with random heights, so it is full of folds sharper than the smoothing angle. Each row of
        // cells has its own vertices: inside a row vertices are shared by index, between rows only by position.
        private static Geometry CrumpledSheet(int width, int height, int seed)
        {
            var random = new System.Random(seed);
            var heights = new float[width + 1, height + 1];
            for (int y = 0; y <= height; y++)
            {
                for (int x = 0; x <= width; x++)
                {
                    heights[x, y] = (float)(random.NextDouble() * 2.0 - 1.0);
                }
            }
            var geometry = new Geometry();
            for (int y = 0; y < height; y++)
            {
                int rowStart = geometry.vertices.Count;
                for (int edge = 0; edge < 2; edge++)
                {
                    for (int x = 0; x <= width; x++)
                    {
                        geometry.Add(new Vector3(x, y + edge, heights[x, y + edge]), new Vector2((float)x / width, (float)(y + edge) / height));
                    }
                }
                for (int x = 0; x < width; x++)
                {
                    int a = rowStart + x, b = a + 1, c = a + width + 1, d = c + 1;
                    geometry.Triangle(a, c, b);
                    geometry.Triangle(b, c, d);
                }
            }
            return geometry;
        }

        // 24 vertices, four to a face.
        private static Geometry Cube()
        {
            Vector3 x = Vector3.right, y = Vector3.up, z = Vector3.forward;
            // normal, then two edge directions whose cross product is that normal
            Vector3[,] faces = { { x, y, z }, { -x, z, y }, { y, z, x }, { -y, x, z }, { z, x, y }, { -z, y, x } };
            var geometry = new Geometry();
            for (int face = 0; face < 6; face++)
            {
                Vector3 normal = faces[face, 0], u = faces[face, 1], v = faces[face, 2];
                Vector3 origin = (normal - u - v) * 0.5f;
                int p0 = geometry.Add(origin, new Vector2(0f, 0f));
                int p1 = geometry.Add(origin + u, new Vector2(1f, 0f));
                int p2 = geometry.Add(origin + u + v, new Vector2(1f, 1f));
                int p3 = geometry.Add(origin + v, new Vector2(0f, 1f));
                geometry.Triangle(p0, p1, p2);
                geometry.Triangle(p0, p2, p3);
            }
            return geometry;
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance, string what)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(tolerance), what + ": expected " + expected.ToString("F5") + " but was " + actual.ToString("F5"));
        }

        [Test]
        public void SmoothSphereGetsOutwardNormalsAndTheUVSeamStaysClosed()
        {
            const int rings = 24, segments = 48;
            Geometry sphere = Sphere(rings, segments);
            Run(sphere, 60f, null, out Vector3[] normals, out _);

            // One of the doubled vertices at each pole is in no triangle (its triangle has no area and is left out).
            var used = new HashSet<int>(sphere.triangles);
            Assert.AreEqual(normals.Length - 2, used.Count);
            foreach (int i in used)
            {
                Assert.That(Vector3.Dot(normals[i], sphere.vertices[i].normalized), Is.GreaterThan(0.995f), "vertex " + i + " should point out from the center");
            }
            int stride = segments + 1;
            for (int ring = 1; ring < rings; ring++)
            {
                AssertNear(normals[ring * stride], normals[ring * stride + segments], 1e-5f, "the two vertices on the seam in ring " + ring);
            }
        }

        [Test]
        public void EdgesSharperThanTheAngleStayHardAndSofterOnesAreSmoothed()
        {
            Geometry cube = Cube();

            Run(cube, 60f, null, out Vector3[] hard, out _);
            for (int i = 0; i < hard.Length; i++)
            {
                // Every vertex of a face lies half a unit out along that face's axis.
                Vector3 p = cube.vertices[i];
                Vector3 faceNormal = (i / 4) switch { 0 => Vector3.right, 1 => Vector3.left, 2 => Vector3.up, 3 => Vector3.down, 4 => Vector3.forward, _ => Vector3.back };
                Assert.That(Vector3.Dot(p, faceNormal), Is.EqualTo(0.5f).Within(1e-6f));
                AssertNear(faceNormal, hard[i], 1e-5f, "90 degree edges at a 60 degree angle, vertex " + i);
            }

            Run(cube, 100f, null, out Vector3[] smooth, out _);
            for (int i = 0; i < smooth.Length; i++)
            {
                Vector3 p = cube.vertices[i];
                Assert.That(smooth[i].x * p.x > 0.1f && smooth[i].y * p.y > 0.1f && smooth[i].z * p.z > 0.1f,"at 100 degrees vertex " + i + " leans to all three faces of its corner: " + smooth[i].ToString("F4"));
                for (int other = 0; other < smooth.Length; other++)
                {
                    if (cube.vertices[other] == p)
                    {
                        AssertNear(smooth[i], smooth[other], 1e-5f, "vertices " + i + " and " + other + " at the same corner");
                    }
                }
            }
        }

        [Test]
        public void NormalsAndTangentsMatchThePlainAlgorithmOnAMeshFullOfFolds()
        {
            Geometry sheet = CrumpledSheet(90, 70, 12345);
            Run(sheet, 60f, null, out Vector3[] normals, out Vector4[] tangents);

            Vector3[] expectedNormals = ReferenceNormals(sheet, 60f, null);
            int folded = 0;
            for (int i = 0; i < normals.Length; i++)
            {
                AssertNear(expectedNormals[i], normals[i], 1e-4f, "normal of vertex " + i);
            }
            // The mesh must really contain what this test is for: vertices at one position with different normals.
            int stride = 91;
            for (int row = 0; row + 1 < 70; row++)
            {
                for (int x = 0; x <= 90; x++)
                {
                    int upperEdgeOfRow = row * 2 * stride + stride + x, lowerEdgeOfNextRow = (row + 1) * 2 * stride + x;
                    Assert.AreEqual(sheet.vertices[upperEdgeOfRow], sheet.vertices[lowerEdgeOfNextRow]);
                    if (Vector3.Distance(normals[upperEdgeOfRow], normals[lowerEdgeOfNextRow]) > 0.05f) folded++;
                }
            }
            Assert.That(folded, Is.GreaterThan(500), "too few hard folds for the comparison to mean anything");

            Vector4[] expectedTangents = ReferenceTangents(sheet, normals);
            for (int i = 0; i < tangents.Length; i++)
            {
                AssertNear(expectedTangents[i], tangents[i], 1e-3f, "tangent of vertex " + i);
                Assert.AreEqual(expectedTangents[i].w, tangents[i].w, "tangent handedness of vertex " + i);
            }
        }

        [Test]
        public void TheSameInputGivesExactlyTheSameOutputEveryTime()
        {
            // Large enough for the parallel jobs to be split over many threads.
            Geometry sheet = CrumpledSheet(160, 120, 777);
            Geometry sphere = Sphere(96, 192);
            foreach (Geometry geometry in new[] { sheet, sphere })
            {
                Run(geometry, 60f, null, out Vector3[] firstNormals, out Vector4[] firstTangents);
                for (int run = 0; run < 6; run++)
                {
                    Run(geometry, 60f, null, out Vector3[] normals, out Vector4[] tangents);
                    for (int i = 0; i < normals.Length; i++)
                    {
                        if (!normals[i].Equals(firstNormals[i]) || !tangents[i].Equals(firstTangents[i]))
                        {
                            Assert.Fail("run " + run + ", vertex " + i + ": normal " + normals[i].ToString("F6") + " vs " + firstNormals[i].ToString("F6") +
                                ", tangent " + tangents[i].ToString("F6") + " vs " + firstTangents[i].ToString("F6"));
                        }
                    }
                }
            }
        }

        [Test]
        public void AVertexNoTriangleUsesKeepsItsNormalAndAMeshWithoutTrianglesIsLeftAlone()
        {
            var geometry = new Geometry();
            int a = geometry.Add(new Vector3(0f, 0f, 0f), new Vector2(0f, 0f));
            int b = geometry.Add(new Vector3(0f, 1f, 0f), new Vector2(0f, 1f));
            int c = geometry.Add(new Vector3(1f, 0f, 0f), new Vector2(1f, 0f));
            int loose = geometry.Add(new Vector3(5f, 5f, 5f), new Vector2(0.5f, 0.5f));
            geometry.Triangle(a, b, c);
            var start = new[] { Vector3.up, Vector3.up, Vector3.up, new Vector3(0f, 0.6f, 0.8f) };

            Run(geometry, 60f, start, out Vector3[] normals, out _);
            AssertNear(Vector3.back, normals[a], 1e-6f, "vertex of the triangle");
            AssertNear(Vector3.back, normals[b], 1e-6f, "vertex of the triangle");
            AssertNear(Vector3.back, normals[c], 1e-6f, "vertex of the triangle");
            Assert.AreEqual(start[loose], normals[loose], "the unused vertex");

            geometry.triangles.Clear();
            Run(geometry, 60f, start, out normals, out _);
            CollectionAssert.AreEqual(start, normals);
        }

        [Test]
        public void TheMeshOverloadWritesNormalsToAMeshThatHadNone()
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(1f, 0f, 0f) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                Assert.AreEqual(0, mesh.normals.Length);

                mesh.RecalculateNormalsTangentsJobified(60f);

                Vector3[] normals = mesh.normals;
                Assert.AreEqual(4, normals.Length);
                for (int i = 0; i < normals.Length; i++)
                {
                    AssertNear(Vector3.back, normals[i], 1e-6f, "vertex " + i);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}

#endif
