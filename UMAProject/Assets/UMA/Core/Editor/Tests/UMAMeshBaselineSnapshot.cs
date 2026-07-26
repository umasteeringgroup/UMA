#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Captures the observable mesh-combiner output used by the incremental
    /// combiner parity tests. Keep this representation independent of a
    /// particular combiner implementation.
    /// </summary>
    [Serializable]
    internal sealed class UMAMeshBaselineSnapshot
    {
        [Serializable]
        internal sealed class SubMeshSnapshot
        {
            public MeshTopology topology;
            public int[] indices;
        }

        [Serializable]
        internal sealed class BlendShapeFrameSnapshot
        {
            public float weight;
            public Vector3[] deltaVertices;
            public Vector3[] deltaNormals;
            public Vector3[] deltaTangents;
        }

        [Serializable]
        internal sealed class BlendShapeSnapshot
        {
            public string name;
            public BlendShapeFrameSnapshot[] frames;
        }

        public string label;
        public int vertexCount;
        public IndexFormat indexFormat;
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Color32[] colors;
        public Vector2[] uv0;
        public Vector2[] uv1;
        public Vector2[] uv2;
        public Vector2[] uv3;
        public Matrix4x4[] bindPoses;
        public byte[] bonesPerVertex;
        public BoneWeight1[] boneWeights;
        public SubMeshSnapshot[] subMeshes;
        public BlendShapeSnapshot[] blendShapes;

        public static UMAMeshBaselineSnapshot Capture(string label, UMAMeshData meshData, IndexFormat indexFormat)
        {
            if (meshData == null)
            {
                throw new ArgumentNullException(nameof(meshData));
            }

            var snapshot = new UMAMeshBaselineSnapshot
            {
                label = label,
                vertexCount = meshData.vertexCount,
                indexFormat = indexFormat,
                vertices = Normalize(meshData.vertices),
                normals = Normalize(meshData.normals),
                tangents = Normalize(meshData.tangents),
                colors = Normalize(meshData.colors32),
                uv0 = Normalize(meshData.uv),
                uv1 = Normalize(meshData.uv2),
                uv2 = Normalize(meshData.uv3),
                uv3 = Normalize(meshData.uv4),
                bindPoses = Normalize(meshData.bindPoses),
                bonesPerVertex = Normalize(meshData.ManagedBonesPerVertex),
                boneWeights = Normalize(meshData.ManagedBoneWeights),
                subMeshes = CaptureSubMeshes(meshData),
                blendShapes = CaptureBlendShapes(meshData)
            };

            return snapshot;
        }

        public static UMAMeshBaselineSnapshot Capture(string label, Mesh mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }
            if (!mesh.isReadable)
            {
                throw new InvalidOperationException($"Mesh '{mesh.name}' must be readable to capture a baseline.");
            }

            NativeArray<byte> nativeBonesPerVertex = default;
            NativeArray<BoneWeight1> nativeBoneWeights = default;
            try
            {
                nativeBonesPerVertex = mesh.GetBonesPerVertex();
                nativeBoneWeights = mesh.GetAllBoneWeights();

                return new UMAMeshBaselineSnapshot
                {
                    label = label,
                    vertexCount = mesh.vertexCount,
                    indexFormat = mesh.indexFormat,
                    vertices = Normalize(mesh.vertices),
                    normals = Normalize(mesh.normals),
                    tangents = Normalize(mesh.tangents),
                    colors = Normalize(mesh.colors32),
                    uv0 = Normalize(mesh.uv),
                    uv1 = Normalize(mesh.uv2),
                    uv2 = Normalize(mesh.uv3),
                    uv3 = Normalize(mesh.uv4),
                    bindPoses = Normalize(mesh.bindposes),
                    bonesPerVertex = nativeBonesPerVertex.IsCreated ? nativeBonesPerVertex.ToArray() : Array.Empty<byte>(),
                    boneWeights = nativeBoneWeights.IsCreated ? nativeBoneWeights.ToArray() : Array.Empty<BoneWeight1>(),
                    subMeshes = CaptureSubMeshes(mesh),
                    blendShapes = CaptureBlendShapes(mesh)
                };
            }
            finally
            {
                if (nativeBonesPerVertex.IsCreated)
                {
                    nativeBonesPerVertex.Dispose();
                }
                if (nativeBoneWeights.IsCreated)
                {
                    nativeBoneWeights.Dispose();
                }
            }
        }

        public string ComputeSha256()
        {
            string json = JsonUtility.ToJson(this, false);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
                var result = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                {
                    result.Append(bytes[i].ToString("x2"));
                }
                return result.ToString();
            }
        }

        public static void AssertEquivalent(
            UMAMeshBaselineSnapshot expected,
            UMAMeshBaselineSnapshot actual,
            float tolerance = 1e-5f)
        {
            Assert.NotNull(expected);
            Assert.NotNull(actual);
            Assert.AreEqual(expected.vertexCount, actual.vertexCount, "Vertex count");
            Assert.AreEqual(expected.indexFormat, actual.indexFormat, "Index format");

            AssertVector3Array(expected.vertices, actual.vertices, tolerance, "Vertices");
            AssertVector3Array(expected.normals, actual.normals, tolerance, "Normals");
            AssertVector4Array(expected.tangents, actual.tangents, tolerance, "Tangents");
            CollectionAssert.AreEqual(expected.colors, actual.colors, "Colors");
            AssertVector2Array(expected.uv0, actual.uv0, tolerance, "UV0");
            AssertVector2Array(expected.uv1, actual.uv1, tolerance, "UV1");
            AssertVector2Array(expected.uv2, actual.uv2, tolerance, "UV2");
            AssertVector2Array(expected.uv3, actual.uv3, tolerance, "UV3");
            AssertMatrixArray(expected.bindPoses, actual.bindPoses, tolerance, "Bind poses");
            CollectionAssert.AreEqual(expected.bonesPerVertex, actual.bonesPerVertex, "Bones per vertex");
            AssertBoneWeightArray(expected.boneWeights, actual.boneWeights, tolerance);

            Assert.AreEqual(expected.subMeshes.Length, actual.subMeshes.Length, "Submesh count");
            for (int i = 0; i < expected.subMeshes.Length; i++)
            {
                Assert.AreEqual(expected.subMeshes[i].topology, actual.subMeshes[i].topology, $"Submesh {i} topology");
                CollectionAssert.AreEqual(expected.subMeshes[i].indices, actual.subMeshes[i].indices, $"Submesh {i} indices");
            }

            Assert.AreEqual(expected.blendShapes.Length, actual.blendShapes.Length, "Blendshape count");
            for (int shapeIndex = 0; shapeIndex < expected.blendShapes.Length; shapeIndex++)
            {
                BlendShapeSnapshot expectedShape = expected.blendShapes[shapeIndex];
                BlendShapeSnapshot actualShape = actual.blendShapes[shapeIndex];
                Assert.AreEqual(expectedShape.name, actualShape.name, $"Blendshape {shapeIndex} name");
                Assert.AreEqual(expectedShape.frames.Length, actualShape.frames.Length, $"Blendshape '{expectedShape.name}' frame count");

                for (int frameIndex = 0; frameIndex < expectedShape.frames.Length; frameIndex++)
                {
                    BlendShapeFrameSnapshot expectedFrame = expectedShape.frames[frameIndex];
                    BlendShapeFrameSnapshot actualFrame = actualShape.frames[frameIndex];
                    Assert.That(
                        actualFrame.weight,
                        Is.EqualTo(expectedFrame.weight).Within(tolerance),
                        $"Blendshape '{expectedShape.name}' frame {frameIndex} weight");
                    AssertVector3Array(
                        expectedFrame.deltaVertices,
                        actualFrame.deltaVertices,
                        tolerance,
                        $"Blendshape '{expectedShape.name}' frame {frameIndex} vertices");
                    AssertVector3Array(
                        expectedFrame.deltaNormals,
                        actualFrame.deltaNormals,
                        tolerance,
                        $"Blendshape '{expectedShape.name}' frame {frameIndex} normals");
                    AssertVector3Array(
                        expectedFrame.deltaTangents,
                        actualFrame.deltaTangents,
                        tolerance,
                        $"Blendshape '{expectedShape.name}' frame {frameIndex} tangents");
                }
            }
        }

        private static SubMeshSnapshot[] CaptureSubMeshes(UMAMeshData meshData)
        {
            if (meshData.submeshes == null || meshData.subMeshCount <= 0)
            {
                return Array.Empty<SubMeshSnapshot>();
            }

            var result = new SubMeshSnapshot[meshData.subMeshCount];
            for (int i = 0; i < result.Length; i++)
            {
                NativeArray<int> triangles = meshData.submeshes[i].GetTriangles();
                result[i] = new SubMeshSnapshot
                {
                    topology = MeshTopology.Triangles,
                    indices = triangles.IsCreated ? triangles.ToArray() : Array.Empty<int>()
                };
            }
            return result;
        }

        private static SubMeshSnapshot[] CaptureSubMeshes(Mesh mesh)
        {
            var result = new SubMeshSnapshot[mesh.subMeshCount];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new SubMeshSnapshot
                {
                    topology = mesh.GetTopology(i),
                    indices = mesh.GetIndices(i)
                };
            }
            return result;
        }

        private static BlendShapeSnapshot[] CaptureBlendShapes(UMAMeshData meshData)
        {
            if (meshData.blendShapes == null)
            {
                return Array.Empty<BlendShapeSnapshot>();
            }

            var result = new BlendShapeSnapshot[meshData.blendShapes.Length];
            for (int shapeIndex = 0; shapeIndex < result.Length; shapeIndex++)
            {
                UMABlendShape shape = meshData.blendShapes[shapeIndex];
                int frameCount = shape?.frames?.Length ?? 0;
                var frames = new BlendShapeFrameSnapshot[frameCount];
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    UMABlendFrame frame = shape.frames[frameIndex];
                    frames[frameIndex] = new BlendShapeFrameSnapshot
                    {
                        weight = frame.frameWeight,
                        deltaVertices = Normalize(frame.deltaVertices),
                        deltaNormals = NormalizeBlendShapeChannel(frame.deltaNormals, meshData.vertexCount),
                        deltaTangents = NormalizeBlendShapeChannel(frame.deltaTangents, meshData.vertexCount)
                    };
                }

                result[shapeIndex] = new BlendShapeSnapshot
                {
                    name = shape?.shapeName ?? string.Empty,
                    frames = frames
                };
            }
            return result;
        }

        private static BlendShapeSnapshot[] CaptureBlendShapes(Mesh mesh)
        {
            var result = new BlendShapeSnapshot[mesh.blendShapeCount];
            for (int shapeIndex = 0; shapeIndex < result.Length; shapeIndex++)
            {
                int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
                var frames = new BlendShapeFrameSnapshot[frameCount];
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    var deltaVertices = new Vector3[mesh.vertexCount];
                    var deltaNormals = new Vector3[mesh.vertexCount];
                    var deltaTangents = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(
                        shapeIndex,
                        frameIndex,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents);

                    frames[frameIndex] = new BlendShapeFrameSnapshot
                    {
                        weight = mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex),
                        deltaVertices = deltaVertices,
                        deltaNormals = deltaNormals,
                        deltaTangents = deltaTangents
                    };
                }

                result[shapeIndex] = new BlendShapeSnapshot
                {
                    name = mesh.GetBlendShapeName(shapeIndex),
                    frames = frames
                };
            }
            return result;
        }

        private static Vector3[] NormalizeBlendShapeChannel(Vector3[] values, int vertexCount)
        {
            if (values == null || values.Length == 0)
            {
                return new Vector3[vertexCount];
            }
            return values;
        }

        private static T[] Normalize<T>(T[] values)
        {
            return values ?? Array.Empty<T>();
        }

        private static void AssertVector2Array(Vector2[] expected, Vector2[] actual, float tolerance, string label)
        {
            Assert.AreEqual(expected.Length, actual.Length, $"{label} length");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i].x, Is.EqualTo(expected[i].x).Within(tolerance), $"{label}[{i}].x");
                Assert.That(actual[i].y, Is.EqualTo(expected[i].y).Within(tolerance), $"{label}[{i}].y");
            }
        }

        private static void AssertVector3Array(Vector3[] expected, Vector3[] actual, float tolerance, string label)
        {
            Assert.AreEqual(expected.Length, actual.Length, $"{label} length");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i].x, Is.EqualTo(expected[i].x).Within(tolerance), $"{label}[{i}].x");
                Assert.That(actual[i].y, Is.EqualTo(expected[i].y).Within(tolerance), $"{label}[{i}].y");
                Assert.That(actual[i].z, Is.EqualTo(expected[i].z).Within(tolerance), $"{label}[{i}].z");
            }
        }

        private static void AssertVector4Array(Vector4[] expected, Vector4[] actual, float tolerance, string label)
        {
            Assert.AreEqual(expected.Length, actual.Length, $"{label} length");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i].x, Is.EqualTo(expected[i].x).Within(tolerance), $"{label}[{i}].x");
                Assert.That(actual[i].y, Is.EqualTo(expected[i].y).Within(tolerance), $"{label}[{i}].y");
                Assert.That(actual[i].z, Is.EqualTo(expected[i].z).Within(tolerance), $"{label}[{i}].z");
                Assert.That(actual[i].w, Is.EqualTo(expected[i].w).Within(tolerance), $"{label}[{i}].w");
            }
        }

        private static void AssertMatrixArray(Matrix4x4[] expected, Matrix4x4[] actual, float tolerance, string label)
        {
            Assert.AreEqual(expected.Length, actual.Length, $"{label} length");
            for (int matrixIndex = 0; matrixIndex < expected.Length; matrixIndex++)
            {
                for (int element = 0; element < 16; element++)
                {
                    Assert.That(
                        actual[matrixIndex][element],
                        Is.EqualTo(expected[matrixIndex][element]).Within(tolerance),
                        $"{label}[{matrixIndex}][{element}]");
                }
            }
        }

        private static void AssertBoneWeightArray(BoneWeight1[] expected, BoneWeight1[] actual, float tolerance)
        {
            Assert.AreEqual(expected.Length, actual.Length, "Bone weight count");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i].boneIndex, actual[i].boneIndex, $"Bone weight {i} index");
                Assert.That(actual[i].weight, Is.EqualTo(expected[i].weight).Within(tolerance), $"Bone weight {i} value");
            }
        }
    }
}

#endif
