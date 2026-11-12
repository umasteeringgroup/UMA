#if UNITY_EDITOR && UMA_BURSTCOMPILE

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.Tests
{
    // The shared recalculation behind UMARendererAsset (Recalculate Normals) and RecalculationSlot. It must give
    // exactly what the earlier managed-array code gave, and may only skip work when nothing it reads has changed.
    public sealed class UMARendererNormalsTests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private static readonly MethodInfo recalculateMethod = typeof(UMAData).Assembly.GetType("UMA.UMARendererNormals")
            .GetMethod("Recalculate", BindingFlags.Static | BindingFlags.NonPublic);

        [TearDown]
        public void TearDown()
        {
            foreach (var item in created) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            created.Clear();
        }

        private static bool Recalculate(SkinnedMeshRenderer renderer, float angle, string prefix, Action<NativeArray<Vector3>> observer = null)
        {
            return (bool)recalculateMethod.Invoke(null, new object[] { renderer, angle, prefix, observer });
        }

        // A sheet with a fold in every row. Rows do not share vertices, so every row border is a seam that the
        // recalculation has to close, and the folds are sharp enough for the angle to matter.
        private SkinnedMeshRenderer Sheet(IndexFormat indexFormat, bool separateStreams, bool withUVs = true, bool withTangents = true, int subMeshes = 2)
        {
            const int width = 9, height = 7;
            var random = new System.Random(12345);
            var heights = new float[width + 1, height + 1];
            for (int y = 0; y <= height; y++)
                for (int x = 0; x <= width; x++)
                    heights[x, y] = (float)random.NextDouble() * 1.5f;

            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>();
            var triangles = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) triangles[i] = new List<int>();
            for (int y = 0; y < height; y++)
            {
                int rowStart = vertices.Count;
                for (int row = 0; row < 2; row++)
                    for (int x = 0; x <= width; x++)
                    {
                        vertices.Add(new Vector3(x, heights[x, y + row], y + row));
                        uvs.Add(new Vector2(x / (float)width, (y + row) / (float)height));
                        normals.Add(Vector3.up);
                        tangents.Add(new Vector4(1, 0, 0, 1));
                    }
                for (int x = 0; x < width; x++)
                {
                    int a = rowStart + x, b = a + 1, c = a + width + 1, d = c + 1;
                    var target = triangles[y % subMeshes];
                    target.Add(a); target.Add(c); target.Add(b);
                    target.Add(b); target.Add(c); target.Add(d);
                }
            }

            var mesh = new Mesh { name = "Normals test sheet", indexFormat = indexFormat };
            created.Add(mesh);
            if (separateStreams)
            {
                // Positions in their own stream, as the jobified combiner lays its meshes out.
                var layout = new List<VertexAttributeDescriptor>
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1)
                };
                if (withTangents) layout.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1));
                if (withUVs) layout.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2));
                mesh.SetVertexBufferParams(vertices.Count, layout.ToArray());
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            if (withTangents) mesh.SetTangents(tangents);
            if (withUVs) mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subMeshes;
            for (int i = 0; i < subMeshes; i++) mesh.SetTriangles(triangles[i], i);

            var first = new Vector3[vertices.Count]; var second = new Vector3[vertices.Count]; var other = new Vector3[vertices.Count];
            // Deltas depend on the position only, so vertices on a seam move together and the seam stays closed.
            for (int i = 0; i < vertices.Count; i++)
            {
                float x = vertices[i].x, z = vertices[i].z;
                first[i] = new Vector3(0, 0.5f * Mathf.Sin(x * 1.3f + z * 0.7f), 0);
                second[i] = new Vector3(0.2f * Mathf.Cos(z * 2.1f), Mathf.Sin(x * 0.9f - z * 1.7f), 0);
                other[i] = new Vector3(0, 3f * Mathf.Cos(x * 0.6f + z * 1.1f), 0);
            }
            mesh.AddBlendShapeFrame("DNA_first", 100f, first, null, null);
            mesh.AddBlendShapeFrame("Expression", 100f, other, null, null);
            mesh.AddBlendShapeFrame("DNA_second", 100f, second, null, null);

            var go = new GameObject("Normals test renderer");
            created.Add(go);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        // What the recalculation did before it read the mesh through MeshData: managed arrays, blendshapes baked
        // in blendshape order, then the same jobs.
        private static void ManagedArrayResult(SkinnedMeshRenderer renderer, float angle, string prefix, out Vector3[] normals, out Vector4[] tangents)
        {
            Mesh mesh = renderer.sharedMesh;
            var nativeVertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
            var nativeNormals = new NativeArray<Vector3>(mesh.normals, Allocator.TempJob);
            var nativeUVs = new NativeArray<Vector2>(mesh.uv, Allocator.TempJob);
            var nativeTangents = new NativeArray<Vector4>(mesh.tangents, Allocator.TempJob);
            var nativeTriangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);
            JobHandle handle = default;
            try
            {
                var delta = new Vector3[mesh.vertexCount];
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    float weight = renderer.GetBlendShapeWeight(shape);
                    if (weight > 0f && mesh.GetBlendShapeName(shape).StartsWith(prefix))
                    {
                        mesh.GetBlendShapeFrameVertices(shape, 0, delta, null, null);
                        handle = MeshUtilities.BakeOneFramePositionBlendShape(nativeVertices, new NativeArray<Vector3>(delta, Allocator.TempJob), weight / 100f, handle);
                    }
                }
                MeshUtilities.RecalculateNormalsTangentsJobified(nativeVertices, nativeNormals, nativeUVs, nativeTangents, nativeTriangles, angle, handle).Complete();
                normals = nativeNormals.ToArray();
                tangents = nativeTangents.ToArray();
            }
            finally
            {
                handle.Complete();
                nativeVertices.Dispose(); nativeNormals.Dispose(); nativeUVs.Dispose(); nativeTangents.Dispose(); nativeTriangles.Dispose();
            }
        }

        [TestCase(IndexFormat.UInt16, false)]
        [TestCase(IndexFormat.UInt32, false)]
        [TestCase(IndexFormat.UInt16, true)]
        [TestCase(IndexFormat.UInt32, true)]
        public void ResultIsExactlyWhatTheManagedArrayCodeGave(IndexFormat indexFormat, bool separateStreams)
        {
            SkinnedMeshRenderer renderer = Sheet(indexFormat, separateStreams);
            Mesh mesh = renderer.sharedMesh;
            renderer.SetBlendShapeWeight(0, 80f);
            renderer.SetBlendShapeWeight(1, 100f); // "Expression": does not start with the prefix, so it is left out.
            renderer.SetBlendShapeWeight(2, 35f);
            Vector3[] verticesBefore = mesh.vertices; Vector2[] uvsBefore = mesh.uv; int[] trianglesBefore = mesh.triangles;
            ManagedArrayResult(renderer, 50f, "DNA_", out Vector3[] expectedNormals, out Vector4[] expectedTangents);
            Assert.That(expectedNormals, Is.Not.EqualTo(mesh.normals), "The test mesh must not start out with the recalculated normals.");

            Vector3[] observed = null;
            Assert.IsTrue(Recalculate(renderer, 50f, "DNA_", normals => observed = normals.ToArray()));

            Assert.That(mesh.normals, Is.EqualTo(expectedNormals));
            Assert.That(mesh.tangents, Is.EqualTo(expectedTangents));
            Assert.That(observed, Is.EqualTo(expectedNormals));
            Assert.That(mesh.vertices, Is.EqualTo(verticesBefore), "Only normals and tangents are written.");
            Assert.That(mesh.uv, Is.EqualTo(uvsBefore));
            Assert.That(mesh.triangles, Is.EqualTo(trianglesBefore));
            Assert.That(mesh.blendShapeCount, Is.EqualTo(3));
        }

        [Test]
        public void ASecondCallWithNothingChangedDoesNoWork()
        {
            SkinnedMeshRenderer renderer = Sheet(IndexFormat.UInt16, true);
            Mesh mesh = renderer.sharedMesh;
            renderer.SetBlendShapeWeight(0, 60f);
            int calls = 0;
            Assert.IsTrue(Recalculate(renderer, 60f, "DNA_", _ => calls++));
            Vector3[] normals = mesh.normals; Vector4[] tangents = mesh.tangents;

            Assert.IsFalse(Recalculate(renderer, 60f, "DNA_", _ => calls++));
            Assert.IsFalse(Recalculate(renderer, 60f, "DNA_", _ => calls++));

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(mesh.normals, Is.EqualTo(normals));
            Assert.That(mesh.tangents, Is.EqualTo(tangents));

            // A weight on a blendshape that takes no part changes nothing either.
            renderer.SetBlendShapeWeight(1, 75f);
            Assert.IsFalse(Recalculate(renderer, 60f, "DNA_", _ => calls++));
        }

        // Every input, changed one at a time after a recalculation. Each change must be noticed, and the result
        // must again be exactly what the managed-array code gives for the new state.
        [TestCase("weight")]
        [TestCase("weight to zero")]
        [TestCase("second blendshape")]
        [TestCase("angle")]
        [TestCase("prefix")]
        [TestCase("vertex position")]
        [TestCase("triangle order")]
        [TestCase("uv")]
        [TestCase("normals overwritten")]
        [TestCase("tangents overwritten")]
        [TestCase("blendshape deltas")]
        [TestCase("another renderer state on the same mesh")]
        public void AnyChangedInputIsRecalculated(string change)
        {
            SkinnedMeshRenderer renderer = Sheet(IndexFormat.UInt16, true);
            Mesh mesh = renderer.sharedMesh;
            renderer.SetBlendShapeWeight(0, 60f);
            float angle = 60f; string prefix = "DNA_";
            Assert.IsTrue(Recalculate(renderer, angle, prefix));
            Assert.IsFalse(Recalculate(renderer, angle, prefix), "Sanity: unchanged inputs are skipped.");
            Vector3[] previousNormals = mesh.normals;

            switch (change)
            {
                case "weight": renderer.SetBlendShapeWeight(0, 61f); break;
                case "weight to zero": renderer.SetBlendShapeWeight(0, 0f); break;
                case "second blendshape": renderer.SetBlendShapeWeight(2, 10f); break;
                case "angle": angle = 20f; break;
                case "prefix": prefix = ""; renderer.SetBlendShapeWeight(1, 40f); break;
                case "vertex position":
                {
                    Vector3[] vertices = mesh.vertices;
                    vertices[17] += new Vector3(0, 0.75f, 0);
                    mesh.SetVertices(vertices);
                    break;
                }
                case "triangle order":
                {
                    int[] triangles = mesh.GetTriangles(1);
                    // Turn one triangle over.
                    int swap = triangles[1]; triangles[1] = triangles[2]; triangles[2] = swap;
                    mesh.SetTriangles(triangles, 1);
                    break;
                }
                case "uv":
                {
                    Vector2[] uvs = mesh.uv;
                    for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(uvs[i].y, uvs[i].x * 0.5f);
                    mesh.SetUVs(0, uvs);
                    break;
                }
                case "normals overwritten":
                {
                    var normals = new Vector3[mesh.vertexCount];
                    for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.right;
                    mesh.SetNormals(normals);
                    break;
                }
                case "tangents overwritten":
                {
                    var tangents = new Vector4[mesh.vertexCount];
                    for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(0, 0, 1, -1);
                    mesh.SetTangents(tangents);
                    break;
                }
                case "blendshape deltas":
                {
                    // Same names, counts and weights; only the deltas of the active blendshape differ.
                    var deltas = new Vector3[3][];
                    for (int shape = 0; shape < 3; shape++)
                    {
                        deltas[shape] = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(shape, 0, deltas[shape], null, null);
                    }
                    for (int i = 0; i < deltas[0].Length; i++) deltas[0][i] *= -2f;
                    mesh.ClearBlendShapes();
                    mesh.AddBlendShapeFrame("DNA_first", 100f, deltas[0], null, null);
                    mesh.AddBlendShapeFrame("Expression", 100f, deltas[1], null, null);
                    mesh.AddBlendShapeFrame("DNA_second", 100f, deltas[2], null, null);
                    renderer.SetBlendShapeWeight(0, 60f);
                    break;
                }
                case "another renderer state on the same mesh":
                {
                    var go = new GameObject("Second renderer");
                    created.Add(go);
                    var second = go.AddComponent<SkinnedMeshRenderer>();
                    second.sharedMesh = mesh;
                    second.SetBlendShapeWeight(0, 5f);
                    renderer = second;
                    break;
                }
            }

            ManagedArrayResult(renderer, angle, prefix, out Vector3[] expectedNormals, out Vector4[] expectedTangents);
            Assert.IsTrue(Recalculate(renderer, angle, prefix), "The change must be noticed.");
            Assert.That(mesh.normals, Is.EqualTo(expectedNormals));
            Assert.That(mesh.tangents, Is.EqualTo(expectedTangents));
            if (change != "uv" && change != "tangents overwritten" && change != "normals overwritten")
            {
                Assert.That(mesh.normals, Is.Not.EqualTo(previousNormals), "Sanity: this change alters the normals.");
            }
            Assert.IsFalse(Recalculate(renderer, angle, prefix), "And the new state is remembered.");
        }

        [Test]
        public void MeshWithoutUVsGetsNormalsOnlyAndMeshWithoutTangentsGetsThem()
        {
            SkinnedMeshRenderer noUVs = Sheet(IndexFormat.UInt16, false, withUVs: false, withTangents: false);
            Assert.IsTrue(Recalculate(noUVs, 60f, ""));
            Assert.IsFalse(noUVs.sharedMesh.HasVertexAttribute(VertexAttribute.Tangent), "Tangents cannot be derived without UVs.");
            Assert.IsFalse(noUVs.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0));
            foreach (Vector3 normal in noUVs.sharedMesh.normals) Assert.That(normal.magnitude, Is.EqualTo(1f).Within(1e-4f));
            Assert.IsFalse(Recalculate(noUVs, 60f, ""));

            SkinnedMeshRenderer noTangents = Sheet(IndexFormat.UInt16, false, withUVs: true, withTangents: false);
            Assert.IsTrue(Recalculate(noTangents, 60f, ""));
            Assert.IsTrue(noTangents.sharedMesh.HasVertexAttribute(VertexAttribute.Tangent));
            foreach (Vector4 tangent in noTangents.sharedMesh.tangents) Assert.That(Mathf.Abs(tangent.w), Is.EqualTo(1f));
            Assert.IsFalse(Recalculate(noTangents, 60f, ""));
        }

        [Test]
        public void MissingEmptyAndUnreadableMeshesAreLeftAlone()
        {
            Assert.IsFalse(Recalculate(null, 60f, ""));

            var go = new GameObject("Empty renderer");
            created.Add(go);
            var emptyRenderer = go.AddComponent<SkinnedMeshRenderer>();
            Assert.IsFalse(Recalculate(emptyRenderer, 60f, ""));
            var emptyMesh = new Mesh();
            created.Add(emptyMesh);
            emptyRenderer.sharedMesh = emptyMesh;
            Assert.IsFalse(Recalculate(emptyRenderer, 60f, ""));

            SkinnedMeshRenderer unreadable = Sheet(IndexFormat.UInt16, true);
            unreadable.sharedMesh.UploadMeshData(true);
            Assert.IsFalse(Recalculate(unreadable, 60f, ""));

            // A null prefix means every blendshape, like an empty one.
            SkinnedMeshRenderer renderer = Sheet(IndexFormat.UInt16, true);
            renderer.SetBlendShapeWeight(1, 50f);
            ManagedArrayResult(renderer, 60f, "", out Vector3[] expectedNormals, out _);
            Assert.IsTrue(Recalculate(renderer, 60f, null));
            Assert.That(renderer.sharedMesh.normals, Is.EqualTo(expectedNormals));
        }

        // The fingerprint that tells whether a kept mesh was written to. It leaves out exactly the normals and
        // tangents, wherever the vertex layout puts them, and sees every other change.
        [TestCase(IndexFormat.UInt16, false)]
        [TestCase(IndexFormat.UInt32, false)]
        [TestCase(IndexFormat.UInt16, true)]
        public void GeometryFingerprintIgnoresOnlyNormalsAndTangents(IndexFormat indexFormat, bool separateStreams)
        {
            var hashGeometry = typeof(UMAData).Assembly.GetType("UMA.UMARendererNormals").GetMethod("HashGeometry", BindingFlags.Static | BindingFlags.NonPublic);
            Mesh mesh = Sheet(indexFormat, separateStreams).sharedMesh;
            ulong Hash() => (ulong)hashGeometry.Invoke(null, new object[] { mesh });
            ulong original = Hash();
            Assert.That(Hash(), Is.EqualTo(original));

            var normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++) normals[i] = new Vector3(0.6f, 0, 0.8f);
            mesh.SetNormals(normals);
            Assert.That(Hash(), Is.EqualTo(original), "Normals are left out.");
            var tangents = mesh.tangents;
            for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(0, 1, 0, -1);
            mesh.SetTangents(tangents);
            Assert.That(Hash(), Is.EqualTo(original), "Tangents are left out.");

            // One value at a time, at the first, a middle and the last vertex.
            foreach (int vertex in new[] { 0, mesh.vertexCount / 2, mesh.vertexCount - 1 })
            {
                var vertices = mesh.vertices;
                vertices[vertex].z += 0.001f;
                mesh.SetVertices(vertices);
                ulong moved = Hash();
                Assert.That(moved, Is.Not.EqualTo(original), "Position of vertex " + vertex);
                vertices[vertex].z -= 0.001f;
                mesh.SetVertices(vertices);
                Assert.That(mesh.vertices[vertex], Is.EqualTo(vertices[vertex]));

                var uvs = mesh.uv;
                Vector2 uvBefore = uvs[vertex];
                uvs[vertex].y += 0.25f;
                mesh.SetUVs(0, uvs);
                Assert.That(Hash(), Is.Not.EqualTo(original), "UV of vertex " + vertex);
                uvs[vertex] = uvBefore;
                mesh.SetUVs(0, uvs);
            }
            Assert.That(Hash(), Is.EqualTo(original), "Everything was put back.");

            int[] triangles = mesh.GetTriangles(0);
            int swap = triangles[0]; triangles[0] = triangles[1]; triangles[1] = swap;
            mesh.SetTriangles(triangles, 0);
            Assert.That(Hash(), Is.Not.EqualTo(original), "Index data.");
        }

        // The generator calls this once per build. A build that changed neither the mesh nor the weights must not
        // recalculate again, and one that did must.
        [Test]
        public void RendererAssetRecalculatesOnlyWhenSomethingChanged()
        {
            const BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var observerField = typeof(UMARendererAsset).GetField("NormalsRecalculatedForTests", nonPublic);
            var finalize = typeof(UMARendererAsset).GetMethod("FinalizeGeneratedNormals", nonPublic);
            var upload = typeof(UMARendererAsset).GetMethod("UploadGeneratedMesh", nonPublic);
            var asset = ScriptableObject.CreateInstance<UMARendererAsset>();
            created.Add(asset);
            typeof(UMARendererAsset).GetField("_RecalculateNormals", nonPublic).SetValue(asset, true);
            typeof(UMARendererAsset).GetField("blendShapeNameStartsWith", nonPublic).SetValue(asset, "DNA_");

            SkinnedMeshRenderer renderer = Sheet(IndexFormat.UInt16, true);
            var avatar = new GameObject("Normals test avatar");
            created.Add(avatar);
            var data = avatar.AddComponent<UMAData>();
            renderer.transform.SetParent(avatar.transform, false);
            data.defaultRendererAsset = asset;
            data.SetRenderers(new[] { renderer });
            data.SetRendererAssets(new UMARendererAsset[] { asset });
            renderer.SetBlendShapeWeight(0, 30f);

            int recalculations = 0;
            try
            {
                observerField.SetValue(null, (Action<SkinnedMeshRenderer, Vector3[]>)((smr, normals) => recalculations++));
                finalize.Invoke(null, new object[] { data });
                Assert.That(recalculations, Is.EqualTo(1));

                finalize.Invoke(null, new object[] { data });
                finalize.Invoke(null, new object[] { data });
                Assert.That(recalculations, Is.EqualTo(1), "Nothing changed between these builds.");

                renderer.SetBlendShapeWeight(0, 31f);
                finalize.Invoke(null, new object[] { data });
                Assert.That(recalculations, Is.EqualTo(2), "DNA changed a blendshape weight.");

                // A combiner handing over a rebuilt mesh starts over, even if the bytes happen to be the same.
                upload.Invoke(null, new object[] { data, renderer, null });
                Assert.That(recalculations, Is.EqualTo(3), "A direct upload recalculates at once.");
                finalize.Invoke(null, new object[] { data });
                Assert.That(recalculations, Is.EqualTo(3));
                Assert.IsTrue(renderer.sharedMesh.isReadable);
            }
            finally
            {
                observerField.SetValue(null, null);
            }
        }
    }
}
#endif
