using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// Data-only tests for <see cref="UMASlotRigConverter"/> on small synthetic rigs. Every
    /// expectation is checked in avatar space by skinning the original and the converted mesh
    /// with their own rigs, so a test passes only when the two meshes land in the same place.
    /// </summary>
    public sealed class UMASlotRigConverterTests
    {
        private const float Tolerance = 1e-5f;

        // --- synthetic rigs -------------------------------------------------------------------

        private static UMATransform Bone(string name, string parent, Vector3 position, Quaternion rotation, Vector3? scale = null)
        {
            return new UMATransform
            {
                name = name,
                hash = UMAUtils.StringToHash(name),
                parent = UMAUtils.StringToHash(parent),
                position = position,
                rotation = rotation,
                scale = scale ?? Vector3.one,
            };
        }

        private static int H(string name) => UMAUtils.StringToHash(name);

        /// <summary>
        /// Converts an offset given in avatar axes into the source rig's Position space, which, like
        /// the UMA 2 rigs, lies along -X until the rig root rotates it upright.
        /// </summary>
        private static Vector3 PS(float x, float y, float z) => new Vector3(-y, x, z);

        /// <summary>Source rig: rotated rig root (character laid along -X in Position space), arms angled 30 degrees.</summary>
        private static List<UMATransform> SourceBones()
        {
            return new List<UMATransform>
            {
                Bone("Position", "Global", Vector3.zero, Quaternion.identity),
                Bone("Hips", "Position", PS(0f, 1.0f, 0.02f), Quaternion.Euler(0f, 0f, 5f)),
                Bone("Spine", "Hips", PS(0f, 0.20f, 0f), Quaternion.Euler(0f, 0f, -5f)),
                Bone("Head", "Spine", PS(0f, 0.30f, 0f), Quaternion.identity),
                Bone("LeftArm", "Spine", PS(-0.20f, 0.10f, 0f), Quaternion.Euler(0f, 0f, 30f)),
                Bone("LeftHand", "LeftArm", PS(-0.30f, 0f, 0f), Quaternion.Euler(0f, 10f, 0f)),
                Bone("Extra", "LeftHand", PS(-0.05f, 0f, 0f), Quaternion.identity),
            };
        }

        /// <summary>Target rig: identity rig root, longer legs, shorter spine, arms angled 40 degrees. No "Extra" bone.</summary>
        private static List<UMATransform> TargetBones()
        {
            return new List<UMATransform>
            {
                Bone("Position", "Global", Vector3.zero, Quaternion.identity),
                Bone("Hips", "Position", new Vector3(0f, 1.05f, -0.01f), Quaternion.Euler(3f, 0f, 0f)),
                Bone("Spine", "Hips", new Vector3(0f, 0.18f, 0f), Quaternion.Euler(-3f, 0f, 0f)),
                Bone("Head", "Spine", new Vector3(0f, 0.31f, 0f), Quaternion.identity),
                Bone("LeftArm", "Spine", new Vector3(-0.22f, 0.10f, 0f), Quaternion.Euler(0f, 0f, 40f)),
                Bone("LeftHand", "LeftArm", new Vector3(-0.28f, 0f, 0f), Quaternion.Euler(0f, 15f, 0f)),
            };
        }

        private static readonly Matrix4x4 SourceRigRoot = Matrix4x4.TRS(new Vector3(0f, 0.01f, -0.01f), Quaternion.Euler(0f, 0f, 270f), Vector3.one);
        private static readonly Matrix4x4 SourceMeshSpace = Matrix4x4.Rotate(Quaternion.Euler(270f, 0f, 0f)); // mesh -> avatar
        private static readonly Matrix4x4 TargetMeshSpace = Matrix4x4.Rotate(Quaternion.Euler(270f, 0f, 0f));

        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        /// <summary>A rig with a donor slot bound to it, so it has a canonical mesh space.</summary>
        private UMARigRestPose MakeRig(List<UMATransform> bones, Matrix4x4 rigRoot, Matrix4x4 meshToAvatar, params string[] donorBoneNames)
        {
            UMARigRestPose bare = UMARigRestPose.FromBones(bones, "Global", rigRoot);
            SlotDataAsset donor = ScriptableObject.CreateInstance<SlotDataAsset>();
            created.Add(donor);
            var md = new UMAMeshData
            {
                SlotName = "Donor",
                RootBoneName = "Global",
                rootBoneHash = H("Global"),
                umaBones = bones.ToArray(),
                umaBoneCount = bones.Count,
                vertexCount = 1,
                vertices = new[] { Vector3.zero },
                boneNameHashes = new int[donorBoneNames.Length],
                bindPoses = new Matrix4x4[donorBoneNames.Length],
                ManagedBonesPerVertex = new byte[] { 1 },
                ManagedBoneWeights = new[] { new BoneWeight1 { boneIndex = 0, weight = 1f } },
            };
            for (int i = 0; i < donorBoneNames.Length; i++)
            {
                md.boneNameHashes[i] = H(donorBoneNames[i]);
                Assert.IsTrue(bare.TryGetAvatarMatrix(md.boneNameHashes[i], out Matrix4x4 boneToAvatar));
                md.bindPoses[i] = boneToAvatar.inverse * meshToAvatar;
            }
            donor.meshData = md;
            return UMARigRestPose.FromSlots(new[] { donor }, "Global", rigRoot);
        }

        private UMARigRestPose SourceRig() => MakeRig(SourceBones(), SourceRigRoot, SourceMeshSpace, "Hips", "Spine", "Head", "LeftArm", "LeftHand");
        private UMARigRestPose TargetRig() => MakeRig(TargetBones(), Matrix4x4.identity, TargetMeshSpace, "Hips", "Spine", "Head", "LeftArm", "LeftHand");

        // --- synthetic meshes -----------------------------------------------------------------

        private sealed class Influence
        {
            public string Bone;
            public Matrix4x4 BindPose;
        }

        /// <summary>
        /// Builds mesh data in <paramref name="meshToAvatar"/> space, bound to <paramref name="rig"/>.
        /// Each influence binds a bone, optionally in a posed configuration (extra rotation of the bone
        /// at bind time), which produces several bind poses for the same bone.
        /// </summary>
        private static UMAMeshData MakeMesh(UMARigRestPose rig, Matrix4x4 meshToAvatar, Influence[] influences,
            Vector3[] avatarPositions, (int index, float weight)[][] weights, bool withTangents = true, UMATransform[] slotBones = null)
        {
            int n = avatarPositions.Length;
            var md = new UMAMeshData
            {
                SlotName = "Synthetic",
                RootBoneName = "Global",
                rootBoneHash = H("Global"),
                vertexCount = n,
                vertices = new Vector3[n],
                normals = new Vector3[n],
                tangents = withTangents ? new Vector4[n] : null,
                boneNameHashes = new int[influences.Length],
                bindPoses = new Matrix4x4[influences.Length],
                ManagedBonesPerVertex = new byte[n],
                umaBones = slotBones ?? new List<UMATransform>(rig.Bones.Values).ToArray(),
            };
            md.umaBoneCount = md.umaBones.Length;
            for (int i = 0; i < influences.Length; i++)
            {
                md.boneNameHashes[i] = H(influences[i].Bone);
                md.bindPoses[i] = influences[i].BindPose;
            }
            // One triangle per three vertices, so winding and triangle ownership can be checked.
            var triangles = new int[(n / 3) * 3];
            for (int t = 0; t < triangles.Length; t++) triangles[t] = t;
            md.submeshes = new[] { new SubMeshTriangles(triangles) };
            md.subMeshCount = 1;

            // Authored positions are given in avatar space for readability; store them in mesh space
            // such that each vertex's own skinning returns exactly that avatar position at rest.
            var list = new List<BoneWeight1>();
            Matrix4x4 avatarToMesh = meshToAvatar.inverse;
            for (int v = 0; v < n; v++)
            {
                Matrix4x4 blended = new Matrix4x4(Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);
                foreach ((int index, float weight) in weights[v])
                {
                    rig.TryGetAvatarMatrix(md.boneNameHashes[index], out Matrix4x4 boneToAvatar);
                    Matrix4x4 skin = boneToAvatar * md.bindPoses[index];
                    for (int k = 0; k < 16; k++) blended[k] += skin[k] * weight;
                    list.Add(new BoneWeight1 { boneIndex = index, weight = weight });
                }
                md.ManagedBonesPerVertex[v] = (byte)weights[v].Length;
                md.vertices[v] = blended.inverse.MultiplyPoint3x4(avatarPositions[v]);
                Vector3 avatarNormal = (avatarPositions[v] - new Vector3(0f, 1.2f, 0f)).normalized;
                Vector3 avatarTangent = Vector3.Cross(avatarNormal, Vector3.up).normalized;
                if (avatarTangent.sqrMagnitude < 0.5f) avatarTangent = Vector3.right;
                Matrix4x4 linear = blended;
                linear.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                // Unity skins a normal with the blended matrix itself, so this is the mesh normal that
                // skins to avatarNormal.
                md.normals[v] = linear.inverse.MultiplyVector(avatarNormal).normalized;
                if (withTangents)
                {
                    Vector3 t = linear.inverse.MultiplyVector(avatarTangent).normalized;
                    md.tangents[v] = new Vector4(t.x, t.y, t.z, v % 2 == 0 ? 1f : -1f);
                }
            }
            md.ManagedBoneWeights = list.ToArray();
            return md;
        }

        private static Matrix4x4 BindPoseFor(UMARigRestPose rig, string bone, Matrix4x4 meshToAvatar, Quaternion? posedRotation = null)
        {
            Assert.IsTrue(rig.TryGetAvatarMatrix(H(bone), out Matrix4x4 boneToAvatar), bone);
            if (posedRotation.HasValue)
            {
                boneToAvatar = boneToAvatar * Matrix4x4.Rotate(posedRotation.Value);
            }
            return boneToAvatar.inverse * meshToAvatar;
        }

        private static Vector3[] Cloud(int count, Vector3 center, float radius)
        {
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.399f;
                points[i] = center + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 0.7f), Mathf.Sin(a)) * radius * (0.5f + 0.5f * ((i % 3) / 2f));
            }
            return points;
        }

        /// <summary>
        /// Skins mesh data with its rig into avatar space the way Unity does (weights as stored, normals
        /// through the blended matrix), optionally with a blend shape frame applied at 100%.
        /// <paramref name="normalMode"/> InverseTranspose bakes normals by that convention instead.
        /// Bitangents are the mesh-space bitangent (cross(n, t) * w) carried as a tangent vector.
        /// </summary>
        private static void Bake(UMAMeshData md, UMARigRestPose rig, out Vector3[] positions, out Vector3[] normals, out Vector4[] tangents,
            out Vector3[] bitangents, int blendShape = -1, UMARigNormalMode normalMode = UMARigNormalMode.MatchSourceSkinning)
        {
            int n = md.vertexCount;
            positions = new Vector3[n];
            normals = new Vector3[n];
            tangents = md.tangents != null ? new Vector4[n] : null;
            bitangents = md.tangents != null ? new Vector3[n] : null;
            var skin = new Matrix4x4[md.boneNameHashes.Length];
            for (int i = 0; i < skin.Length; i++)
            {
                Assert.IsTrue(rig.TryGetAvatarMatrix(md.boneNameHashes[i], out Matrix4x4 boneToAvatar), "rig lacks bone for influence " + i);
                skin[i] = boneToAvatar * md.bindPoses[i];
            }
            UMABlendFrame frame = blendShape >= 0 ? md.blendShapes[blendShape].frames[0] : null;
            int offset = 0;
            for (int v = 0; v < n; v++)
            {
                Matrix4x4 blended = new Matrix4x4(Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);
                int count = md.ManagedBonesPerVertex[v];
                for (int k = 0; k < count; k++)
                {
                    BoneWeight1 bw = md.ManagedBoneWeights[offset + k];
                    for (int m = 0; m < 16; m++) blended[m] += skin[bw.boneIndex][m] * bw.weight;
                }
                offset += count;
                Vector3 p = md.vertices[v];
                Vector3 nrm = md.normals != null && md.normals.Length == n ? md.normals[v] : Vector3.up;
                if (frame != null)
                {
                    p += frame.deltaVertices[v];
                    if (frame.deltaNormals != null && frame.deltaNormals.Length == n) nrm += frame.deltaNormals[v];
                }
                positions[v] = blended.MultiplyPoint3x4(p);
                Matrix4x4 linear = blended;
                linear.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                Matrix4x4 normalMatrix = normalMode == UMARigNormalMode.InverseTranspose ? linear.inverse.transpose : linear;
                normals[v] = normalMatrix.MultiplyVector(nrm).normalized;
                if (tangents != null)
                {
                    Vector4 t4 = md.tangents[v];
                    Vector3 t = t4;
                    if (frame != null && frame.deltaTangents != null && frame.deltaTangents.Length == n) t += frame.deltaTangents[v];
                    Vector3 tw = linear.MultiplyVector(t).normalized;
                    tangents[v] = new Vector4(tw.x, tw.y, tw.z, t4.w);
                    bitangents[v] = linear.MultiplyVector(Vector3.Cross(nrm.normalized, t.normalized) * t4.w).normalized;
                }
            }
        }

        private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance, string what)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThanOrEqualTo(tolerance), $"{what}: expected {expected:F6} got {actual:F6}");
        }

        private static void AssertSamePlace(UMAMeshData original, UMARigRestPose originalRig, UMAMeshData converted, UMARigRestPose convertedRig, float tolerance, int blendShape = -1,
            UMARigNormalMode normalMode = UMARigNormalMode.MatchSourceSkinning)
        {
            Bake(original, originalRig, out Vector3[] p0, out Vector3[] n0, out Vector4[] t0, out Vector3[] b0, blendShape, normalMode);
            Bake(converted, convertedRig, out Vector3[] p1, out Vector3[] n1, out Vector4[] t1, out Vector3[] b1, blendShape, normalMode);
            for (int v = 0; v < p0.Length; v++)
            {
                AssertClose(p0[v], p1[v], tolerance, "position " + v);
                AssertClose(n0[v], n1[v], tolerance * 10f, "normal " + v);
                if (t0 != null)
                {
                    AssertClose(t0[v], t1[v], tolerance * 10f, "tangent " + v);
                    Assert.AreEqual(t0[v].w, t1[v].w, "tangent handedness " + v);
                    // Bitangents are only compared where the per-vertex map is a rotation (single
                    // influence); a blend of several bones is not orthogonal, so cross(n, t) does not
                    // transform as a vector there and the GPU recomputes it from n and t anyway.
                    if (original.ManagedBonesPerVertex[v] == 1)
                    {
                        AssertClose(b0[v], b1[v], tolerance * 10f, "bitangent " + v);
                    }
                }
            }
        }

        // --- tests ----------------------------------------------------------------------------

        [Test]
        public void SingleBoneSlotRoundTripsExactly()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) } };
            Vector3[] points = Cloud(12, new Vector3(0f, 1.0f, 0.05f), 0.2f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            UMAMeshData onTarget = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact);
            UMAMeshData backHome = UMASlotRigConverter.Convert(onTarget, target, source, UMASlotRigConversionOptions.Exact);

            Assert.AreEqual(1, onTarget.boneNameHashes.Length);
            Assert.AreEqual(H("Hips"), onTarget.boneNameHashes[0]);
            Assert.AreEqual(H("Global"), onTarget.rootBoneHash);
            Assert.IsTrue(target.TryGetAvatarMatrix(H("Hips"), out Matrix4x4 hipsToAvatar));
            Assert.That(UMARigRestPose.MaxAbsDifference(hipsToAvatar.inverse * TargetMeshSpace, onTarget.bindPoses[0]), Is.LessThan(Tolerance));

            for (int v = 0; v < points.Length; v++)
            {
                AssertClose(original.vertices[v], backHome.vertices[v], Tolerance, "vertex " + v);
                AssertClose(original.normals[v], backHome.normals[v], Tolerance * 10f, "normal " + v);
                AssertClose(original.tangents[v], backHome.tangents[v], Tolerance * 10f, "tangent " + v);
                Assert.AreEqual(original.tangents[v].w, backHome.tangents[v].w);
            }
            Assert.That(UMARigRestPose.MaxAbsDifference(original.bindPoses[0], backHome.bindPoses[0]), Is.LessThan(Tolerance));
            AssertSamePlace(original, source, onTarget, target, Tolerance);
        }

        [Test]
        public void TwoBindPosesForOneBoneBakeToTheSamePlace()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            // The second influence bound the same bone while it was rotated 25 degrees: two bind poses, one bone.
            var influences = new[]
            {
                new Influence { Bone = "LeftArm", BindPose = BindPoseFor(source, "LeftArm", SourceMeshSpace) },
                new Influence { Bone = "LeftArm", BindPose = BindPoseFor(source, "LeftArm", SourceMeshSpace, Quaternion.Euler(0f, 0f, 25f)) },
                new Influence { Bone = "LeftHand", BindPose = BindPoseFor(source, "LeftHand", SourceMeshSpace) },
            };
            source.TryGetAvatarPosition(H("LeftArm"), out Vector3 arm);
            Vector3[] points = Cloud(15, arm + new Vector3(-0.15f, 0f, 0f), 0.08f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++)
            {
                weights[v] = (v % 3) switch
                {
                    0 => new[] { (0, 1f) },
                    1 => new[] { (1, 1f) },
                    _ => new[] { (0, 0.4f), (1, 0.35f), (2, 0.25f) },
                };
            }
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            Bake(original, source, out Vector3[] baked, out _, out _, out _);
            // Sanity: vertices on the posed bind pose are not already where a single bind pose would put them.
            Matrix4x4 single = SourceRigRootTimes(source, "LeftArm") * influences[0].BindPose;
            Assert.That(Vector3.Distance(single.MultiplyPoint3x4(original.vertices[1]), baked[1]), Is.GreaterThan(0.01f), "test data must contain a second bind pose that matters");

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            Assert.AreEqual(2, converted.boneNameHashes.Length, "one entry per target bone");
            CollectionAssert.AreEquivalent(new[] { H("LeftArm"), H("LeftHand") }, converted.boneNameHashes);
            Assert.AreEqual(0, report.WeightsMovedToAncestor);
            AssertSamePlace(original, source, converted, target, Tolerance);
            // Weights were merged per bone and renormalised.
            int offset = 0;
            for (int v = 0; v < points.Length; v++)
            {
                float sum = 0f;
                for (int k = 0; k < converted.ManagedBonesPerVertex[v]; k++) sum += converted.ManagedBoneWeights[offset + k].weight;
                offset += converted.ManagedBonesPerVertex[v];
                Assert.AreEqual(1f, sum, 1e-5f, "weights sum " + v);
                Assert.That(converted.ManagedBonesPerVertex[v], Is.LessThanOrEqualTo(2));
            }
        }

        private static Matrix4x4 SourceRigRootTimes(UMARigRestPose rig, string bone)
        {
            rig.TryGetAvatarMatrix(H(bone), out Matrix4x4 m);
            return m;
        }

        [Test]
        public void BlendShapesDeformTheSameInAvatarSpace()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[]
            {
                new Influence { Bone = "Spine", BindPose = BindPoseFor(source, "Spine", SourceMeshSpace) },
                new Influence { Bone = "Head", BindPose = BindPoseFor(source, "Head", SourceMeshSpace, Quaternion.Euler(10f, 0f, 0f)) },
            };
            source.TryGetAvatarPosition(H("Head"), out Vector3 head);
            Vector3[] points = Cloud(10, head, 0.1f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = v % 2 == 0 ? new[] { (1, 1f) } : new[] { (0, 0.5f), (1, 0.5f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            var frame = new UMABlendFrame(points.Length, true, true);
            for (int v = 0; v < points.Length; v++)
            {
                frame.deltaVertices[v] = new Vector3(0.01f * v, 0.02f, -0.015f);
                frame.deltaNormals[v] = new Vector3(0.1f, -0.05f * v, 0.02f);
                frame.deltaTangents[v] = new Vector3(0.0f, 0.03f, 0.01f * v);
            }
            original.blendShapes = new[] { new UMABlendShape { shapeName = "Puff", frames = new[] { frame } } };

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            Assert.AreEqual(1, report.BlendShapeCount);
            Assert.AreEqual("Puff", converted.blendShapes[0].shapeName);
            Assert.AreEqual(100f, converted.blendShapes[0].frames[0].frameWeight);
            AssertSamePlace(original, source, converted, target, Tolerance);
            AssertSamePlace(original, source, converted, target, Tolerance, blendShape: 0);
            Assert.AreNotSame(original.blendShapes[0].frames[0].deltaVertices, converted.blendShapes[0].frames[0].deltaVertices);
        }

        [Test]
        public void MirroredTargetRigFlipsTangentHandedness()
        {
            UMARigRestPose source = SourceRig();
            Matrix4x4 mirror = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            UMARigRestPose target = MakeRig(TargetBones(), mirror, mirror, "Hips", "Spine", "Head", "LeftArm", "LeftHand");
            var influences = new[] { new Influence { Bone = "Spine", BindPose = BindPoseFor(source, "Spine", SourceMeshSpace) } };
            Vector3[] points = Cloud(8, new Vector3(0.1f, 1.3f, 0f), 0.1f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            Assert.IsTrue(report.HandednessFlipped, "the target rig root is mirrored and the source's is not");
            Bake(original, source, out Vector3[] p0, out Vector3[] n0, out Vector4[] t0, out Vector3[] b0);
            Bake(converted, target, out Vector3[] p1, out Vector3[] n1, out Vector4[] t1, out Vector3[] b1);
            for (int v = 0; v < points.Length; v++)
            {
                AssertClose(p0[v], p1[v], Tolerance, "position " + v);
                AssertClose(n0[v], n1[v], Tolerance * 10f, "normal " + v);
                AssertClose((Vector3)t0[v], (Vector3)t1[v], Tolerance * 10f, "tangent " + v);
                Assert.AreEqual(-original.tangents[v].w, converted.tangents[v].w, "handedness must flip under a mirrored map, vertex " + v);
                // The converted mesh-space bitangent (cross(n, t) * w), carried into avatar space, must be the original's.
                AssertClose(b0[v], b1[v], Tolerance * 10f, "bitangent " + v);
            }

            // Unity reverses culling for a renderer under a mirrored root, so a mesh that faces outwards
            // in the source's mesh space must face outwards in the target's mesh space too: every
            // triangle's winding is reversed, and its face normal keeps its side of the vertex normals.
            int[] sourceTriangles = original.submeshes[0].getManagedTriangles(-1);
            int[] convertedTriangles = converted.submeshes[0].getManagedTriangles(-1);
            Assert.AreEqual(sourceTriangles.Length, convertedTriangles.Length);
            Assert.That(sourceTriangles.Length, Is.GreaterThanOrEqualTo(6));
            for (int t = 0; t < sourceTriangles.Length; t += 3)
            {
                Assert.AreEqual(sourceTriangles[t], convertedTriangles[t]);
                Assert.AreEqual(sourceTriangles[t + 1], convertedTriangles[t + 2]);
                Assert.AreEqual(sourceTriangles[t + 2], convertedTriangles[t + 1]);
                Assert.AreEqual(FacingSign(original, sourceTriangles, t), FacingSign(converted, convertedTriangles, t), "triangle " + (t / 3) + " must keep its facing in mesh space");
            }
        }

        /// <summary>Which side of its vertex normals a triangle's face normal lies on, in mesh space.</summary>
        private static int FacingSign(UMAMeshData md, int[] triangles, int offset)
        {
            Vector3 a = md.vertices[triangles[offset]];
            Vector3 b = md.vertices[triangles[offset + 1]];
            Vector3 c = md.vertices[triangles[offset + 2]];
            Vector3 face = Vector3.Cross(b - a, c - a);
            Vector3 average = md.normals[triangles[offset]] + md.normals[triangles[offset + 1]] + md.normals[triangles[offset + 2]];
            return Vector3.Dot(face, average) >= 0f ? 1 : -1;
        }

        [Test]
        public void MirroredMeshSpaceAloneKeepsWindingAndHandedness()
        {
            // Only the target's mesh space is mirrored; its rig root is not. Unity does not compensate
            // for that (the renderer's own transform is not mirrored), so nothing may be flipped.
            UMARigRestPose source = SourceRig();
            Matrix4x4 mirror = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            UMARigRestPose target = MakeRig(TargetBones(), Matrix4x4.identity, mirror, "Hips", "Spine", "Head", "LeftArm", "LeftHand");
            var influences = new[] { new Influence { Bone = "Spine", BindPose = BindPoseFor(source, "Spine", SourceMeshSpace) } };
            Vector3[] points = Cloud(9, new Vector3(0.1f, 1.3f, 0f), 0.1f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            Assert.IsFalse(report.HandednessFlipped);
            CollectionAssert.AreEqual(original.submeshes[0].getManagedTriangles(-1), converted.submeshes[0].getManagedTriangles(-1));
            Bake(original, source, out Vector3[] p0, out Vector3[] n0, out Vector4[] t0, out _);
            Bake(converted, target, out Vector3[] p1, out Vector3[] n1, out Vector4[] t1, out _);
            for (int v = 0; v < points.Length; v++)
            {
                AssertClose(p0[v], p1[v], Tolerance, "position " + v);
                AssertClose(n0[v], n1[v], Tolerance * 10f, "normal " + v);
                AssertClose((Vector3)t0[v], (Vector3)t1[v], Tolerance * 10f, "tangent " + v);
                Assert.AreEqual(original.tangents[v].w, converted.tangents[v].w, "handedness " + v);
            }
        }

        [Test]
        public void MissingTargetBonesRemapWeightsToNearestAncestor()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[]
            {
                new Influence { Bone = "LeftHand", BindPose = BindPoseFor(source, "LeftHand", SourceMeshSpace) },
                new Influence { Bone = "Extra", BindPose = BindPoseFor(source, "Extra", SourceMeshSpace) },
            };
            source.TryGetAvatarPosition(H("Extra"), out Vector3 extra);
            Vector3[] points = Cloud(9, extra, 0.05f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = v % 3 == 0 ? new[] { (1, 1f) } : new[] { (0, 0.3f), (1, 0.7f) };
            // The slot's own umaBones list the race bones only, so Extra is a race bone, not a slot bone.
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights, slotBones: new List<UMATransform>(source.Bones.Values).ToArray());

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            CollectionAssert.AreEqual(new[] { H("LeftHand") }, converted.boneNameHashes);
            Assert.That(report.WeightsMovedToAncestor, Is.GreaterThan(0));
            Assert.AreEqual(0, report.WeightsMovedToRoot);
            Assert.AreEqual(0, report.CarriedBones);
            Assert.That(report.RemappedBones, Has.Some.EqualTo("Extra -> LeftHand"));
            for (int v = 0; v < points.Length; v++)
            {
                Assert.AreEqual(1, converted.ManagedBonesPerVertex[v]);
                Assert.AreEqual(1f, converted.ManagedBoneWeights[v].weight, 1e-6f);
            }
            AssertSamePlace(original, source, converted, target, Tolerance);
        }

        [Test]
        public void SlotOwnedBonesAreCarriedWithTheirRestFrame()
        {
            UMARigRestPose source = MakeRig(SourceBones().FindAll(b => b.name != "Extra"), SourceRigRoot, SourceMeshSpace, "Hips", "LeftHand");
            UMARigRestPose target = TargetRig();
            UMATransform prop = Bone("Prop", "LeftHand", new Vector3(-0.08f, 0.02f, 0f), Quaternion.Euler(0f, 0f, 45f));
            var slotBones = new List<UMATransform>(source.Bones.Values) { prop };
            UMARigRestPose sourceWithProp = source.WithAdditionalBones(new[] { prop });
            var influences = new[]
            {
                new Influence { Bone = "LeftHand", BindPose = BindPoseFor(sourceWithProp, "LeftHand", SourceMeshSpace) },
                new Influence { Bone = "Prop", BindPose = BindPoseFor(sourceWithProp, "Prop", SourceMeshSpace) },
            };
            sourceWithProp.TryGetAvatarPosition(H("Prop"), out Vector3 propJoint);
            Vector3[] points = Cloud(9, propJoint, 0.05f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = v % 2 == 0 ? new[] { (1, 1f) } : new[] { (0, 0.5f), (1, 0.5f) };
            UMAMeshData original = MakeMesh(sourceWithProp, SourceMeshSpace, influences, points, weights, slotBones: slotBones.ToArray());

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            Assert.AreEqual(1, report.CarriedBones);
            CollectionAssert.AreEquivalent(new[] { H("LeftHand"), H("Prop") }, converted.boneNameHashes);
            UMATransform carried = System.Array.Find(converted.umaBones, b => b.hash == H("Prop"));
            Assert.IsNotNull(carried, "carried bone must be defined in umaBones");
            Assert.AreEqual(H("LeftHand"), carried.parent);
            Assert.AreEqual("Prop", carried.name);
            // The generator will build Prop from the converted umaBones; its avatar frame must be where the source rig had it.
            UMARigRestPose targetWithProp = target.WithAdditionalBones(converted.umaBones);
            Assert.IsTrue(targetWithProp.TryGetAvatarMatrix(H("Prop"), out Matrix4x4 propOnTarget));
            Assert.IsTrue(sourceWithProp.TryGetAvatarMatrix(H("Prop"), out Matrix4x4 propOnSource));
            Assert.That(UMARigRestPose.MaxAbsDifference(propOnTarget, propOnSource), Is.LessThan(Tolerance * 10f));
            AssertSamePlace(original, sourceWithProp, converted, targetWithProp, Tolerance);
        }

        [Test]
        public void JointAnchoredCorrectionMovesVerticesWithTheirJoint()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[]
            {
                new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) },
                new Influence { Bone = "LeftArm", BindPose = BindPoseFor(source, "LeftArm", SourceMeshSpace) },
            };
            source.TryGetAvatarPosition(H("Hips"), out Vector3 hipsS);
            source.TryGetAvatarPosition(H("LeftArm"), out Vector3 armS);
            source.TryGetAvatarPosition(H("LeftHand"), out Vector3 handS);
            target.TryGetAvatarPosition(H("Hips"), out Vector3 hipsT);
            target.TryGetAvatarPosition(H("LeftArm"), out Vector3 armT);
            target.TryGetAvatarPosition(H("LeftHand"), out Vector3 handT);
            Vector3[] points = { hipsS + new Vector3(0.1f, 0.05f, 0.1f), armS + new Vector3(-0.1f, 0.02f, 0.03f), hipsS + new Vector3(-0.1f, 0f, 0f) };
            var weights = new[] { new[] { (0, 1f) }, new[] { (1, 1f) }, new[] { (0, 1f) } };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default, report);
            Bake(converted, target, out Vector3[] p1, out _, out _, out _);

            // Each vertex rotates about its joint by the change in bone direction (joint to child) and
            // moves with the joint, keeping its distance from it.
            source.TryGetAvatarPosition(H("Spine"), out Vector3 spineS);
            target.TryGetAvatarPosition(H("Spine"), out Vector3 spineT);
            Quaternion hipsRotation = Quaternion.FromToRotation((spineS - hipsS).normalized, (spineT - hipsT).normalized);
            AssertClose(hipsT + hipsRotation * (points[0] - hipsS), p1[0], Tolerance, "hips vertex");
            AssertClose(hipsT + hipsRotation * (points[2] - hipsS), p1[2], Tolerance, "second hips vertex");
            Assert.That(Quaternion.Angle(Quaternion.identity, hipsRotation), Is.LessThan(15f), "test rigs must stay roughly upright");
            Quaternion armRotation = Quaternion.FromToRotation((handS - armS).normalized, (handT - armT).normalized);
            AssertClose(armT + armRotation * (points[1] - armS), p1[1], Tolerance, "arm vertex");
            Assert.AreEqual(Vector3.Distance(points[1], armS), Vector3.Distance(p1[1], armT), Tolerance);
            Assert.That(Quaternion.Angle(Quaternion.identity, armRotation), Is.InRange(2f, 30f), "the arm direction change must be real but modest");
            Assert.That(report.MaxJointDelta, Is.GreaterThan(0.01f));
        }

        [Test]
        public void ProfileOverridesChangeTheCorrection()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) } };
            source.TryGetAvatarPosition(H("Hips"), out Vector3 hipsS);
            target.TryGetAvatarPosition(H("Hips"), out Vector3 hipsT);
            Vector3[] points = { hipsS + new Vector3(0.1f, 0f, 0f) };
            var weights = new[] { new[] { (0, 1f) } };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            profile.boneCorrections.Add(new UMARigBoneCorrection { boneName = "Hips", mode = UMARigBoneCorrectionMode.None });
            var options = new UMASlotRigConversionOptions { Profile = profile };
            UMAMeshData untouched = UMASlotRigConverter.Convert(original, source, target, options);
            Bake(untouched, target, out Vector3[] p, out _, out _, out _);
            AssertClose(points[0], p[0], Tolerance, "None keeps the baked position");

            profile.boneCorrections[0].mode = UMARigBoneCorrectionMode.TranslateOnly;
            profile.boneCorrections[0].positionOffset = new Vector3(0f, 0f, 0.05f);
            profile.InvalidateLookup();
            UMAMeshData offset = UMASlotRigConverter.Convert(original, source, target, options);
            Bake(offset, target, out p, out _, out _, out _);
            AssertClose(points[0] + (hipsT - hipsS) + new Vector3(0f, 0f, 0.05f), p[0], Tolerance, "TranslateOnly plus offset");
        }

        [Test]
        public void LegacyFourBoneWeightsAreAccepted()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[]
            {
                new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) },
                new Influence { Bone = "Spine", BindPose = BindPoseFor(source, "Spine", SourceMeshSpace) },
            };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.15f, 0f), 0.1f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 0.6f), (1, 0.4f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            var legacy = new UMABoneWeight[points.Length];
            for (int v = 0; v < points.Length; v++) legacy[v] = new UMABoneWeight { boneIndex0 = 0, weight0 = 0.6f, boneIndex1 = 1, weight1 = 0.4f };
            UMAMeshData legacyMesh = original.DeepCopy();
            legacyMesh.ManagedBoneWeights = null;
            legacyMesh.ManagedBonesPerVertex = null;
            legacyMesh.boneWeights = legacy;

            UMAMeshData converted = UMASlotRigConverter.Convert(legacyMesh, source, target, UMASlotRigConversionOptions.Exact);

            Assert.IsNull(converted.boneWeights);
            Assert.AreEqual(points.Length, converted.ManagedBonesPerVertex.Length);
            Assert.IsNotNull(legacyMesh.boneWeights, "the source must not be modified");
            Assert.IsNull(legacyMesh.ManagedBoneWeights, "the source must not be modified");
            AssertSamePlace(original, source, converted, target, Tolerance);
        }

        // --- normals ----------------------------------------------------------------------------

        /// <summary>A slot whose vertices blend two bind poses of one bone 120 degrees apart: the blended matrix is far from a rotation.</summary>
        private UMAMeshData MakeNonRigidMesh(UMARigRestPose source)
        {
            var influences = new[]
            {
                new Influence { Bone = "LeftArm", BindPose = BindPoseFor(source, "LeftArm", SourceMeshSpace) },
                new Influence { Bone = "LeftArm", BindPose = BindPoseFor(source, "LeftArm", SourceMeshSpace, Quaternion.Euler(0f, 0f, 120f)) },
                new Influence { Bone = "LeftHand", BindPose = BindPoseFor(source, "LeftHand", SourceMeshSpace) },
            };
            source.TryGetAvatarPosition(H("LeftArm"), out Vector3 arm);
            Vector3[] points = Cloud(15, arm + new Vector3(-0.15f, 0.03f, 0.02f), 0.08f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++)
            {
                weights[v] = (v % 3) switch
                {
                    0 => new[] { (0, 0.5f), (1, 0.5f) },
                    1 => new[] { (0, 0.7f), (1, 0.3f) },
                    _ => new[] { (0, 0.4f), (1, 0.35f), (2, 0.25f) },
                };
            }
            return MakeMesh(source, SourceMeshSpace, influences, points, weights);
        }

        [Test]
        public void NormalsFollowTheSourceSkinningWhereTheBlendIsNotRigid()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            UMAMeshData original = MakeNonRigidMesh(source);

            UMAMeshData matched = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact);
            var inverseTransposeOptions = new UMASlotRigConversionOptions { CorrectionMode = UMARigCorrectionMode.None, NormalMode = UMARigNormalMode.InverseTranspose };
            UMAMeshData inverseTransposed = UMASlotRigConverter.Convert(original, source, target, inverseTransposeOptions);

            // Each mode reproduces the source under its own convention...
            AssertSamePlace(original, source, matched, target, Tolerance);
            AssertSamePlace(original, source, inverseTransposed, target, Tolerance, normalMode: UMARigNormalMode.InverseTranspose);
            // ...and the two conventions really do differ on this mesh, so the test above can tell them apart.
            float maxAngle = 0f;
            for (int v = 0; v < original.vertexCount; v++)
            {
                maxAngle = Mathf.Max(maxAngle, Vector3.Angle(matched.normals[v], inverseTransposed.normals[v]));
            }
            Assert.That(maxAngle, Is.GreaterThan(5f), "the test mesh must contain blends that are not rotations");
        }

        [Test]
        public void BlendShapeNormalsFollowTheChosenNormalMode()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            UMAMeshData original = MakeNonRigidMesh(source);
            var frame = new UMABlendFrame(original.vertexCount, true, true);
            for (int v = 0; v < original.vertexCount; v++)
            {
                frame.deltaVertices[v] = new Vector3(0.01f, 0.002f * v, -0.01f);
                frame.deltaNormals[v] = new Vector3(0.2f, -0.03f * v, 0.1f);
                frame.deltaTangents[v] = new Vector3(0.05f, 0.03f, 0.01f * v);
            }
            original.blendShapes = new[] { new UMABlendShape { shapeName = "Bulge", frames = new[] { frame } } };

            UMAMeshData matched = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact);
            var inverseTransposeOptions = new UMASlotRigConversionOptions { CorrectionMode = UMARigCorrectionMode.None, NormalMode = UMARigNormalMode.InverseTranspose };
            UMAMeshData inverseTransposed = UMASlotRigConverter.Convert(original, source, target, inverseTransposeOptions);

            AssertSamePlace(original, source, matched, target, Tolerance, blendShape: 0);
            AssertSamePlace(original, source, inverseTransposed, target, Tolerance, blendShape: 0, normalMode: UMARigNormalMode.InverseTranspose);
        }

        // --- against Unity's own skinning -------------------------------------------------------

        /// <summary>Builds a GameObject skeleton for a rig under <paramref name="avatar"/> and returns its transforms by bone hash.</summary>
        private static Dictionary<int, Transform> BuildSkeleton(Transform avatar, UMARigRestPose rig)
        {
            var map = new Dictionary<int, Transform>();
            var rootObject = new GameObject(rig.RootBoneName);
            rootObject.transform.SetParent(avatar, false);
            Matrix4x4 root = rig.RigRootToAvatar;
            rootObject.transform.localPosition = root.GetColumn(3);
            rootObject.transform.localRotation = root.rotation;
            rootObject.transform.localScale = root.lossyScale;
            map.Add(rig.RootBoneHash, rootObject.transform);

            var pending = new List<UMATransform>(rig.Bones.Values);
            int guard = 0;
            while (pending.Count > 0 && guard++ < 1024)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    UMATransform bone = pending[i];
                    if (!map.TryGetValue(bone.parent, out Transform parent))
                    {
                        continue;
                    }
                    var boneObject = new GameObject(bone.name);
                    boneObject.transform.SetParent(parent, false);
                    boneObject.transform.localPosition = bone.position;
                    boneObject.transform.localRotation = bone.rotation;
                    boneObject.transform.localScale = bone.scale;
                    map.Add(bone.hash, boneObject.transform);
                    pending.RemoveAt(i);
                }
            }
            Assert.AreEqual(0, pending.Count, "every bone must find its parent");
            return map;
        }

        /// <summary>Skins mesh data with a real SkinnedMeshRenderer on a GameObject copy of the rig; results are in avatar space.</summary>
        private void SkinWithUnity(UMAMeshData md, UMARigRestPose rig, out Vector3[] positions, out Vector3[] normals, out Vector4[] tangents)
        {
            var avatar = new GameObject("RigConverterTestAvatar");
            created.Add(avatar);
            Dictionary<int, Transform> skeleton = BuildSkeleton(avatar.transform, rig.WithAdditionalBones(md.umaBones));

            var mesh = new Mesh();
            created.Add(mesh);
            mesh.vertices = md.vertices;
            mesh.normals = md.normals;
            mesh.tangents = md.tangents;
            mesh.SetIndices(md.submeshes[0].getManagedTriangles(-1), MeshTopology.Triangles, 0);
            var perVertex = new Unity.Collections.NativeArray<byte>(md.ManagedBonesPerVertex, Unity.Collections.Allocator.Temp);
            var weights = new Unity.Collections.NativeArray<BoneWeight1>(md.ManagedBoneWeights, Unity.Collections.Allocator.Temp);
            mesh.SetBoneWeights(perVertex, weights);
            perVertex.Dispose();
            weights.Dispose();
            mesh.bindposes = md.bindPoses;

            var bones = new Transform[md.boneNameHashes.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.IsTrue(skeleton.TryGetValue(md.boneNameHashes[i], out bones[i]), "skeleton lacks bone for influence " + i);
            }

            var holder = new GameObject("Renderer");
            holder.transform.SetParent(avatar.transform, false);
            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.quality = SkinQuality.Bone4;
            var baked = new Mesh();
            created.Add(baked);
            renderer.BakeMesh(baked, true);
            positions = baked.vertices;
            normals = baked.normals;
            tangents = baked.tangents;
        }

        [Test]
        public void ConvertedSlotSkinsLikeTheSourceInAUnityRenderer()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            UMAMeshData original = MakeNonRigidMesh(source);
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact);

            SkinWithUnity(original, source, out Vector3[] p0, out Vector3[] n0, out Vector4[] t0);
            SkinWithUnity(converted, target, out Vector3[] p1, out Vector3[] n1, out Vector4[] t1);

            // Unity itself agrees with the model the other tests use...
            Bake(original, source, out Vector3[] modelPositions, out Vector3[] modelNormals, out _, out _);
            for (int v = 0; v < p0.Length; v++)
            {
                AssertClose(modelPositions[v], p0[v], Tolerance * 10f, "Unity vs model position " + v);
                Assert.That(Vector3.Angle(modelNormals[v], n0[v]), Is.LessThan(0.05f), "Unity vs model normal " + v);
            }
            // ...and draws the converted slot on the target rig where it draws the original on the source rig.
            for (int v = 0; v < p0.Length; v++)
            {
                AssertClose(p0[v], p1[v], Tolerance * 10f, "position " + v);
                Assert.That(Vector3.Angle(n0[v], n1[v]), Is.LessThan(0.05f), "normal " + v);
                Assert.That(Vector3.Angle((Vector3)t0[v], (Vector3)t1[v]), Is.LessThan(0.05f), "tangent " + v);
                Assert.AreEqual(t0[v].w, t1[v].w, "tangent handedness " + v);
            }
        }

        // --- weights ----------------------------------------------------------------------------

        [Test]
        public void UnweightedVerticesStayInTheSlotsMeshSpaceWithOneWarning()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) } };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.0f, 0.05f), 0.2f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            // Strip the weights from vertices 1 and 4.
            original.ManagedBonesPerVertex = new byte[] { 1, 0, 1, 1, 0, 1 };
            original.ManagedBoneWeights = new BoneWeight1[4];
            for (int i = 0; i < 4; i++) original.ManagedBoneWeights[i] = new BoneWeight1 { boneIndex = 0, weight = 1f };

            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            Assert.AreEqual(2, report.UnweightedVertices);
            Assert.AreEqual(1, report.Warnings.Count, string.Join(" | ", report.Warnings));
            StringAssert.Contains("2 vertices", report.Warnings[0]);
            CollectionAssert.Contains(converted.boneNameHashes, H("Global"));
            Bake(converted, target, out Vector3[] p, out _, out _, out _);
            for (int v = 0; v < points.Length; v++)
            {
                // The weightless vertices land where the slot's mesh space puts them, with the rest.
                AssertClose(points[v], p[v], Tolerance, "vertex " + v);
                Assert.AreEqual(1, converted.ManagedBonesPerVertex[v]);
            }
        }

        [Test]
        public void WeightsThatDoNotSumToOneFollowUnityOrAreRepaired()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[]
            {
                new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) },
                new Influence { Bone = "Spine", BindPose = BindPoseFor(source, "Spine", SourceMeshSpace, Quaternion.Euler(0f, 20f, 0f)) },
            };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.15f, 0f), 0.1f);

            // Within the tolerance: Unity skins with the weights as stored, and so does the bake.
            var nearlyOne = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) nearlyOne[v] = new[] { (0, 0.6f), (1, 0.3996f) };
            UMAMeshData slightlyOff = MakeMesh(source, SourceMeshSpace, influences, points, nearlyOne);
            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(slightlyOff, source, target, UMASlotRigConversionOptions.Exact, report);
            Assert.AreEqual(0, report.VerticesWithRenormalisedWeights);
            Assert.AreEqual(0, report.Warnings.Count, string.Join(" | ", report.Warnings));
            Bake(slightlyOff, source, out Vector3[] raw, out _, out _, out _);
            Bake(converted, target, out Vector3[] p, out _, out _, out _);
            for (int v = 0; v < points.Length; v++) AssertClose(raw[v], p[v], Tolerance, "as stored, vertex " + v);

            // Far outside it: the weights are damaged. They are normalised first, and the report says so once.
            var damagedWeights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) damagedWeights[v] = new[] { (0, 0.3f), (1, 0.3f) };
            UMAMeshData damaged = MakeMesh(source, SourceMeshSpace, influences, points, damagedWeights);
            report = new UMASlotRigConversionReport();
            converted = UMASlotRigConverter.Convert(damaged, source, target, UMASlotRigConversionOptions.Exact, report);
            Assert.AreEqual(points.Length, report.VerticesWithRenormalisedWeights);
            Assert.AreEqual(1, report.Warnings.Count, string.Join(" | ", report.Warnings));
            Bake(damaged, source, out raw, out _, out _, out _);
            Bake(converted, target, out p, out _, out _, out _);
            for (int v = 0; v < points.Length; v++) AssertClose(raw[v] / 0.6f, p[v], Tolerance, "normalised, vertex " + v);
            int offset = 0;
            for (int v = 0; v < points.Length; v++)
            {
                float sum = 0f;
                for (int k = 0; k < converted.ManagedBonesPerVertex[v]; k++) sum += converted.ManagedBoneWeights[offset + k].weight;
                offset += converted.ManagedBonesPerVertex[v];
                Assert.AreEqual(1f, sum, 1e-5f, "output weights sum " + v);
            }
        }

        // --- options and robustness -------------------------------------------------------------

        [Test]
        public void SlotOwnedBonesGiveTheirWeightsToTheParentWhenNotCarried()
        {
            UMARigRestPose source = MakeRig(SourceBones().FindAll(b => b.name != "Extra"), SourceRigRoot, SourceMeshSpace, "Hips", "LeftHand");
            UMARigRestPose target = TargetRig();
            UMATransform prop = Bone("Prop", "LeftHand", new Vector3(-0.08f, 0.02f, 0f), Quaternion.Euler(0f, 0f, 45f));
            var slotBones = new List<UMATransform>(source.Bones.Values) { prop };
            UMARigRestPose sourceWithProp = source.WithAdditionalBones(new[] { prop });
            var influences = new[]
            {
                new Influence { Bone = "LeftHand", BindPose = BindPoseFor(sourceWithProp, "LeftHand", SourceMeshSpace) },
                new Influence { Bone = "Prop", BindPose = BindPoseFor(sourceWithProp, "Prop", SourceMeshSpace) },
            };
            sourceWithProp.TryGetAvatarPosition(H("Prop"), out Vector3 propJoint);
            Vector3[] points = Cloud(9, propJoint, 0.05f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = v % 2 == 0 ? new[] { (1, 1f) } : new[] { (0, 0.5f), (1, 0.5f) };
            UMAMeshData original = MakeMesh(sourceWithProp, SourceMeshSpace, influences, points, weights, slotBones: slotBones.ToArray());

            var options = new UMASlotRigConversionOptions { CorrectionMode = UMARigCorrectionMode.None, CarrySlotBones = false };
            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, options, report);

            Assert.AreEqual(0, report.CarriedBones);
            CollectionAssert.AreEqual(new[] { H("LeftHand") }, converted.boneNameHashes);
            Assert.That(report.WeightsMovedToAncestor, Is.GreaterThan(0));
            Assert.IsNull(System.Array.Find(converted.umaBones, b => b.hash == H("Prop")), "the prop bone must not be carried");
            AssertSamePlace(original, sourceWithProp, converted, target, Tolerance);
        }

        [Test]
        public void InheritParentUsesTheParentBonesCorrection()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "LeftHand", BindPose = BindPoseFor(source, "LeftHand", SourceMeshSpace) } };
            source.TryGetAvatarPosition(H("LeftArm"), out Vector3 armS);
            source.TryGetAvatarPosition(H("LeftHand"), out Vector3 handS);
            target.TryGetAvatarPosition(H("LeftArm"), out Vector3 armT);
            target.TryGetAvatarPosition(H("LeftHand"), out Vector3 handT);
            Vector3[] points = { handS + new Vector3(-0.03f, 0.01f, 0.02f), handS + new Vector3(0.02f, -0.01f, 0f), handS + new Vector3(0f, 0.02f, -0.02f) };
            var weights = new[] { new[] { (0, 1f) }, new[] { (0, 1f) }, new[] { (0, 1f) } };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            profile.boneCorrections.Add(new UMARigBoneCorrection { boneName = "LeftHand", mode = UMARigBoneCorrectionMode.InheritParent });
            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, new UMASlotRigConversionOptions { Profile = profile }, report);
            Bake(converted, target, out Vector3[] p, out _, out _, out _);

            // The hand's vertices take the arm's correction: rotate about the arm joint by the arm's
            // change of direction, and move with the arm joint.
            Quaternion armRotation = Quaternion.FromToRotation((handS - armS).normalized, (handT - armT).normalized);
            for (int v = 0; v < points.Length; v++)
            {
                AssertClose(armT + armRotation * (points[v] - armS), p[v], Tolerance, "vertex " + v);
            }
            Assert.AreEqual("LeftArm", report.MaxJointDeltaBone, "the joint the slot follows is the arm's");
            Assert.AreEqual(Vector3.Distance(armS, armT), report.MaxJointDelta, 1e-6f);
        }

        [Test]
        public void JointDeltaIsNotReportedForAParentThatOnlyLendsItsRotation()
        {
            // B is a leaf: it has no direction of its own, so it borrows A's rotation but keeps its own
            // joint. A's joint moves ten times further than B's and must not be what the report names.
            var sourceBones = new List<UMATransform>
            {
                Bone("A", "Global", new Vector3(0f, 1f, 0f), Quaternion.identity),
                Bone("B", "A", new Vector3(0f, 0.2f, 0f), Quaternion.identity),
            };
            var targetBones = new List<UMATransform>
            {
                Bone("A", "Global", new Vector3(0f, 1.5f, 0f), Quaternion.identity),
                Bone("B", "A", new Vector3(0f, -0.25f, 0f), Quaternion.identity),
            };
            UMARigRestPose source = UMARigRestPose.FromBones(sourceBones, "Global", Matrix4x4.identity);
            UMARigRestPose target = UMARigRestPose.FromBones(targetBones, "Global", Matrix4x4.identity);
            var influences = new[] { new Influence { Bone = "B", BindPose = BindPoseFor(source, "B", Matrix4x4.identity) } };
            Vector3[] points = { new Vector3(0.05f, 1.2f, 0f), new Vector3(-0.05f, 1.22f, 0.01f), new Vector3(0f, 1.18f, -0.03f) };
            var weights = new[] { new[] { (0, 1f) }, new[] { (0, 1f) }, new[] { (0, 1f) } };
            UMAMeshData original = MakeMesh(source, Matrix4x4.identity, influences, points, weights);

            var report = new UMASlotRigConversionReport();
            UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default, report);

            Assert.AreEqual("B", report.MaxJointDeltaBone);
            Assert.AreEqual(0.05f, report.MaxJointDelta, 1e-5f);
        }

        [Test]
        public void MissingNormalsAndTangentsAreLeftMissing()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) } };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.0f, 0.05f), 0.2f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights, withTangents: false);
            original.normals = null;

            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default);

            Assert.IsNull(converted.normals);
            Assert.IsNull(converted.tangents);
            Bake(converted, target, out Vector3[] p, out _, out _, out _);
            Assert.AreEqual(points.Length, p.Length);
        }

        [Test]
        public void UnusableSlotsAreReportedByTryConvertAndThrownByConvert()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) } };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.0f, 0.05f), 0.2f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };

            // A usable slot converts through both entry points.
            UMAMeshData good = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            Assert.IsTrue(UMASlotRigConverter.CanConvert(good, out string noReason));
            Assert.IsNull(noReason);
            Assert.IsTrue(UMASlotRigConverter.TryConvert(good, source, target, UMASlotRigConversionOptions.Exact, null, out UMAMeshData result, out noReason));
            Assert.IsNotNull(result);
            AssertSamePlace(good, source, result, target, Tolerance);

            // Not skinned at all.
            UMAMeshData unskinned = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            unskinned.bindPoses = null;
            Assert.IsFalse(UMASlotRigConverter.TryConvert(unskinned, source, target, null, null, out result, out string reason));
            Assert.IsNull(result);
            StringAssert.Contains("bind poses", reason);
            Assert.Throws<System.ArgumentException>(() => UMASlotRigConverter.Convert(unskinned, source, target));

            // A bone weight stream that stops short.
            UMAMeshData truncated = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            truncated.ManagedBoneWeights = new[] { new BoneWeight1 { boneIndex = 0, weight = 1f }, new BoneWeight1 { boneIndex = 0, weight = 1f } };
            Assert.IsFalse(UMASlotRigConverter.TryConvert(truncated, source, target, null, null, out result, out reason));
            StringAssert.Contains("stream ends before vertex 2", reason);
            Assert.Throws<System.ArgumentException>(() => UMASlotRigConverter.Convert(truncated, source, target));

            // No mesh, no rig.
            Assert.IsFalse(UMASlotRigConverter.TryConvert(null, source, target, null, null, out result, out reason));
            Assert.IsNotNull(reason);
            Assert.IsFalse(UMASlotRigConverter.TryConvert(good, null, target, null, null, out result, out reason));
            Assert.IsNotNull(reason);
            Assert.IsFalse(UMARigRestPose.TryFromRace(null, out UMARigRestPose noRig, out reason));
            Assert.IsNull(noRig);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void ResultSharesNothingWithTheSource()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            UMAMeshData original = MakeNonRigidMesh(source);
            int[] sourceTriangles = (int[])original.submeshes[0].getManagedTriangles(-1).Clone();
            Vector3[] sourceVertices = (Vector3[])original.vertices.Clone();

            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default);

            Assert.AreNotSame(original.submeshes, converted.submeshes);
            Assert.AreNotSame(original.submeshes[0], converted.submeshes[0]);
            Assert.AreNotSame(original.submeshes[0].getManagedTriangles(-1), converted.submeshes[0].getManagedTriangles(-1));
            CollectionAssert.AreEqual(sourceTriangles, converted.submeshes[0].getManagedTriangles(-1));
            Assert.AreNotSame(original.vertices, converted.vertices);
            Assert.AreNotSame(original.normals, converted.normals);
            Assert.AreNotSame(original.tangents, converted.tangents);
            Assert.AreNotSame(original.bindPoses, converted.bindPoses);
            foreach (UMATransform bone in converted.umaBones)
            {
                Assert.IsFalse(target.Bones.TryGetValue(bone.hash, out UMATransform rigBone) && ReferenceEquals(rigBone, bone), "bone definitions must be copies: " + bone.name);
            }
            Assert.IsFalse(converted.LoadedBoneweights, "native bone weights are not loaded yet; the slot must load them itself");
            // Changing the result leaves the source alone.
            converted.submeshes[0].getManagedTriangles(-1)[0] = 2;
            CollectionAssert.AreEqual(sourceTriangles, original.submeshes[0].getManagedTriangles(-1));
            CollectionAssert.AreEqual(sourceVertices, original.vertices);
        }

        // --- profile offsets: scale and carrying to children -----------------------------------------

        [Test]
        public void ProfileScaleActsAboutTheJointAlongTheBonesOwnAxes()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) } };
            Vector3[] points = Cloud(6, new Vector3(0.05f, 1.0f, 0.05f), 0.2f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            // Mode None keeps the computed correction out of the way, so only the scale acts.
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            var scale = new Vector3(2f, 1f, 0.5f);
            profile.boneCorrections.Add(new UMARigBoneCorrection { boneName = "Hips", mode = UMARigBoneCorrectionMode.None, scaleOffset = scale });
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, new UMASlotRigConversionOptions { Profile = profile });

            Assert.IsTrue(target.TryGetAvatarMatrix(H("Hips"), out Matrix4x4 hipsToAvatar));
            Vector3 joint = hipsToAvatar.GetColumn(3);
            Matrix4x4 frame = Matrix4x4.Rotate(hipsToAvatar.rotation);
            Matrix4x4 scaleInAvatar = frame * Matrix4x4.Scale(scale) * frame.inverse;
            Matrix4x4 normalInAvatar = frame * Matrix4x4.Scale(new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z)) * frame.inverse;
            Bake(original, source, out Vector3[] p0, out Vector3[] n0, out Vector4[] t0, out _);
            Bake(converted, target, out Vector3[] p1, out Vector3[] n1, out Vector4[] t1, out _);
            for (int v = 0; v < points.Length; v++)
            {
                AssertClose(joint + scaleInAvatar.MultiplyVector(p0[v] - joint), p1[v], Tolerance, "position " + v);
                // A normal crosses a scale inverted, so it stays perpendicular to the scaled surface;
                // a tangent lies in the surface and scales with it.
                AssertClose(normalInAvatar.MultiplyVector(n0[v]).normalized, n1[v], Tolerance * 10f, "normal " + v);
                AssertClose(scaleInAvatar.MultiplyVector((Vector3)t0[v]).normalized, (Vector3)t1[v], Tolerance * 10f, "tangent " + v);
            }

            // (1, 1, 1), and an unset (0, 0, 0) from an entry saved before scale existed, change nothing.
            profile.boneCorrections[0].scaleOffset = Vector3.zero;
            profile.InvalidateLookup();
            Assert.IsFalse(profile.boneCorrections[0].HasOffsets);
            UMAMeshData unscaled = UMASlotRigConverter.Convert(original, source, target, new UMASlotRigConversionOptions { Profile = profile });
            Bake(unscaled, target, out p1, out _, out _, out _);
            for (int v = 0; v < points.Length; v++) AssertClose(p0[v], p1[v], Tolerance, "unscaled position " + v);
        }

        [Test]
        public void ProfileOffsetsCarryDownToChildBonesWhenAsked()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "LeftHand", BindPose = BindPoseFor(source, "LeftHand", SourceMeshSpace) } };
            source.TryGetAvatarPosition(H("LeftHand"), out Vector3 handS);
            target.TryGetAvatarPosition(H("LeftArm"), out Vector3 armT);
            Vector3[] points = { handS + new Vector3(-0.03f, 0.01f, 0.02f), handS + new Vector3(0.02f, -0.01f, 0f), handS + new Vector3(0f, 0.02f, -0.02f) };
            var weights = new[] { new[] { (0, 1f) }, new[] { (0, 1f) }, new[] { (0, 1f) } };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            // Where the hand's vertices go with no profile at all.
            UMAMeshData plain = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default);
            Bake(plain, target, out Vector3[] withoutProfile, out _, out _, out _);

            // An offset on the arm, which the slot is not weighted to.
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            var armEntry = profile.GetOrAddCorrection("LeftArm");
            armEntry.rotationOffsetEuler = new Vector3(0f, 0f, 20f);
            armEntry.positionOffset = new Vector3(0.01f, 0f, -0.02f);
            Assert.IsTrue(armEntry.includeChildren, "new entries carry to their children");
            var options = new UMASlotRigConversionOptions { Profile = profile };

            UMAMeshData carried = UMASlotRigConverter.Convert(original, source, target, options);
            Bake(carried, target, out Vector3[] p, out _, out _, out _);
            Quaternion turn = Quaternion.Euler(armEntry.rotationOffsetEuler);
            for (int v = 0; v < points.Length; v++)
            {
                // The hand's vertices swing about the arm's joint and shift with it, as if the arm bone had moved.
                AssertClose(armEntry.positionOffset + armT + turn * (withoutProfile[v] - armT), p[v], Tolerance, "carried vertex " + v);
            }

            armEntry.includeChildren = false;
            profile.InvalidateLookup();
            UMAMeshData notCarried = UMASlotRigConverter.Convert(original, source, target, options);
            Bake(notCarried, target, out p, out _, out _, out _);
            for (int v = 0; v < points.Length; v++) AssertClose(withoutProfile[v], p[v], Tolerance, "uncarried vertex " + v);

            // An entry on the bone itself always applies, and a parent's carried offset acts after it.
            armEntry.includeChildren = true;
            var handEntry = profile.GetOrAddCorrection("LeftHand");
            handEntry.positionOffset = new Vector3(0f, 0.03f, 0f);
            profile.InvalidateLookup();
            UMAMeshData both = UMASlotRigConverter.Convert(original, source, target, options);
            Bake(both, target, out p, out _, out _, out _);
            for (int v = 0; v < points.Length; v++)
            {
                Vector3 afterHand = withoutProfile[v] + handEntry.positionOffset;
                AssertClose(armEntry.positionOffset + armT + turn * (afterHand - armT), p[v], Tolerance, "hand then arm, vertex " + v);
            }
        }

        [Test]
        public void ProfileEntriesAndFavoritesCanBeEditedByBoneName()
        {
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            profile.defaultMode = UMARigBoneCorrectionMode.TranslateOnly;

            Assert.IsFalse(profile.TryGetCorrection("Spine", out _));
            UMARigBoneCorrection entry = profile.GetOrAddCorrection("Spine");
            Assert.AreEqual(UMARigBoneCorrectionMode.TranslateOnly, entry.mode, "a new entry starts on the profile's default mode, so adding it changes nothing");
            Assert.IsFalse(entry.HasOffsets);
            Assert.AreSame(entry, profile.GetOrAddCorrection("Spine"));
            Assert.IsTrue(profile.TryGetCorrection("Spine", out UMARigBoneCorrection found));
            Assert.AreSame(entry, found);

            entry.scaleOffset = new Vector3(1.1f, 1f, 1f);
            Assert.IsTrue(entry.HasScale);
            Assert.IsTrue(entry.HasOffsets);
            entry.ResetOffsets();
            Assert.IsFalse(entry.HasOffsets);
            Assert.AreEqual(UMARigBoneCorrectionMode.TranslateOnly, entry.mode, "resetting keeps the mode");

            Assert.IsTrue(profile.RemoveCorrection("Spine"));
            Assert.IsFalse(profile.RemoveCorrection("Spine"));
            Assert.IsFalse(profile.TryGetCorrection("Spine", out _));

            // Favourites are kept in the profile, so they survive with it.
            profile.SetFavorite("LeftArmAdjust", true);
            profile.SetFavorite("LeftArmAdjust", true);
            profile.SetFavorite("Hips", true);
            Assert.AreEqual(2, profile.favoriteBones.Count);
            UMASlotRigConversionProfile copy = Object.Instantiate(profile);
            created.Add(copy);
            Assert.IsTrue(copy.IsFavorite("LeftArmAdjust"));
            Assert.IsTrue(copy.IsFavorite("Hips"));
            profile.SetFavorite("Hips", false);
            Assert.IsFalse(profile.IsFavorite("Hips"));
            Assert.IsFalse(profile.IsFavorite(null));
        }

        // --- entries for single slots ------------------------------------------------------------

        [Test]
        public void ASlotsOwnEntryReplacesTheGeneralOneForThatSlotOnly()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[]
            {
                new Influence { Bone = "LeftArm", BindPose = BindPoseFor(source, "LeftArm", SourceMeshSpace) },
                new Influence { Bone = "LeftHand", BindPose = BindPoseFor(source, "LeftHand", SourceMeshSpace) },
            };
            source.TryGetAvatarPosition(H("LeftArm"), out Vector3 armS);
            source.TryGetAvatarPosition(H("LeftHand"), out Vector3 handS);
            Vector3[] points = { armS + new Vector3(0.02f, 0.01f, 0f), handS + new Vector3(-0.01f, 0.02f, 0.01f) };
            var weights = new[] { new[] { (0, 1f) }, new[] { (1, 1f) } };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            // Where the vertices go with the computed correction, and with none at all.
            Bake(UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default), target, out Vector3[] corrected, out _, out _, out _);
            Bake(UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact), target, out Vector3[] uncorrected, out _, out _, out _);
            Assert.That(Vector3.Distance(corrected[1], uncorrected[1]), Is.GreaterThan(1e-3f), "the test needs the hand's correction to matter");

            // The entries every slot follows: the arm shifts, the hand is left uncorrected and shifts.
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            UMARigBoneCorrection generalArm = profile.GetOrAddCorrection("LeftArm");
            generalArm.includeChildren = false;
            generalArm.positionOffset = new Vector3(0.01f, 0f, 0f);
            UMARigBoneCorrection generalHand = profile.GetOrAddCorrection("LeftHand");
            generalHand.mode = UMARigBoneCorrectionMode.None;
            generalHand.positionOffset = new Vector3(0f, 0.02f, 0f);

            // The jacket gets a hand entry of its own. It starts as a copy, so making it changes nothing.
            UMARigBoneCorrection jacketHand = profile.GetOrAddCorrection("LeftHand", "Jacket");
            Assert.AreNotSame(generalHand, jacketHand);
            Assert.AreEqual(generalHand.mode, jacketHand.mode);
            Assert.AreEqual(generalHand.positionOffset, jacketHand.positionOffset);
            jacketHand.mode = UMARigBoneCorrectionMode.Full;
            jacketHand.positionOffset = new Vector3(0f, 0f, 0.05f);
            profile.InvalidateLookup();

            var options = new UMASlotRigConversionOptions { Profile = profile, ProfileSlotName = "Jacket" };
            Bake(UMASlotRigConverter.Convert(original, source, target, options), target, out Vector3[] p, out _, out _, out _);
            AssertClose(corrected[0] + generalArm.positionOffset, p[0], Tolerance, "jacket: the arm has no entry of its own and follows the general one");
            AssertClose(corrected[1] + jacketHand.positionOffset, p[1], Tolerance, "jacket: the hand follows its own entry, mode and offset both");

            options.ProfileSlotName = "Shirt";
            Bake(UMASlotRigConverter.Convert(original, source, target, options), target, out p, out _, out _, out _);
            AssertClose(corrected[0] + generalArm.positionOffset, p[0], Tolerance, "shirt: arm");
            AssertClose(uncorrected[1] + generalHand.positionOffset, p[1], Tolerance, "shirt: the hand follows the general entry; the jacket's is not its business");

            // With no name given, the mesh data's own slot name picks the entries.
            options.ProfileSlotName = null;
            original.SlotName = "Jacket";
            Bake(UMASlotRigConverter.Convert(original, source, target, options), target, out p, out _, out _, out _);
            AssertClose(corrected[1] + jacketHand.positionOffset, p[1], Tolerance, "named by the mesh data");

            // A profile holding nothing but one slot's entries still applies them, to that slot alone.
            var slotOnly = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(slotOnly);
            slotOnly.GetOrAddCorrection("LeftHand", "Jacket").positionOffset = new Vector3(0f, 0f, 0.05f);
            options = new UMASlotRigConversionOptions { Profile = slotOnly, ProfileSlotName = "Jacket" };
            Bake(UMASlotRigConverter.Convert(original, source, target, options), target, out p, out _, out _, out _);
            AssertClose(corrected[0], p[0], Tolerance, "slot-only profile: arm untouched");
            AssertClose(corrected[1] + new Vector3(0f, 0f, 0.05f), p[1], Tolerance, "slot-only profile: hand moved");
            options.ProfileSlotName = "Shirt";
            Bake(UMASlotRigConverter.Convert(original, source, target, options), target, out p, out _, out _, out _);
            AssertClose(corrected[1], p[1], Tolerance, "slot-only profile: another slot is untouched");
        }

        [Test]
        public void SlotEntriesAreKeptApartFromTheGeneralOnes()
        {
            var empty = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(empty);
            Assert.IsFalse(empty.HasAnyCorrections(null));
            Assert.IsFalse(empty.HasAnyCorrections("Jacket"));

            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            UMARigBoneCorrection general = profile.GetOrAddCorrection("LeftArm");
            general.positionOffset = new Vector3(0.01f, 0.02f, 0.03f);
            general.rotationOffsetEuler = new Vector3(5f, 10f, 15f);

            // No slot name means the general entries; a slot without an entry of its own sees them too.
            Assert.AreSame(general, profile.GetOrAddCorrection("LeftArm", null));
            Assert.IsTrue(profile.TryGetCorrection("LeftArm", "Jacket", out UMARigBoneCorrection seen));
            Assert.AreSame(general, seen);
            Assert.IsFalse(profile.HasSlotCorrection("LeftArm", "Jacket"));
            Assert.IsNull(profile.GetSlotOverride("Jacket"));

            UMARigBoneCorrection own = profile.GetOrAddCorrection("LeftArm", "Jacket");
            Assert.AreNotSame(general, own);
            Assert.AreEqual(general.rotationOffsetEuler, own.rotationOffsetEuler, "a slot's entry starts as a copy of the general one");
            Assert.AreSame(own, profile.GetOrAddCorrection("LeftArm", "Jacket"));
            Assert.IsTrue(profile.HasSlotCorrection("LeftArm", "Jacket"));
            Assert.IsFalse(profile.HasSlotCorrection("LeftArm", "Shirt"));
            Assert.IsTrue(profile.HasAnyCorrections("Shirt"), "the general entries reach every slot");
            Assert.AreEqual(1, profile.boneCorrections.Count, "the general entries gained nothing");

            // Mirroring inside a slot writes that slot's entry for the other side.
            own.positionOffset = new Vector3(0.04f, 0f, 0f);
            own.rotationOffsetEuler = Vector3.zero;
            UMARigBoneCorrection mirrored = profile.MirrorCorrection("LeftArm", null, "Jacket");
            Assert.IsNotNull(mirrored);
            AssertClose(new Vector3(-0.04f, 0f, 0f), mirrored.positionOffset, Tolerance, "mirrored offset");
            Assert.IsTrue(profile.HasSlotCorrection("RightArm", "Jacket"));
            Assert.IsFalse(profile.TryGetCorrection("RightArm", out _), "mirroring a slot leaves the general entries alone");
            Assert.AreEqual(1, profile.MirrorSide(true, null, "Jacket"));
            Assert.AreEqual(0, profile.MirrorSide(true, null, "Shirt"), "a slot with nothing of its own has nothing to mirror");
            Assert.AreEqual(1, profile.MirrorSide(true, null), "the general side still mirrors on its own");
            Assert.IsTrue(profile.TryGetCorrection("RightArm", out UMARigBoneCorrection generalRight));
            AssertClose(new Vector3(-0.01f, 0.02f, 0.03f), generalRight.positionOffset, Tolerance, "general mirror");

            // Slot entries are saved with the profile.
            UMASlotRigConversionProfile copy = Object.Instantiate(profile);
            created.Add(copy);
            Assert.IsTrue(copy.HasSlotCorrection("RightArm", "Jacket"));
            Assert.IsTrue(copy.TryGetCorrection("LeftArm", "Jacket", out UMARigBoneCorrection copied));
            AssertClose(new Vector3(0.04f, 0f, 0f), copied.positionOffset, Tolerance, "copied slot entry");

            // Removing a slot's entry puts the bone back on the general one.
            Assert.IsTrue(profile.RemoveCorrection("LeftArm", "Jacket"));
            Assert.IsFalse(profile.HasSlotCorrection("LeftArm", "Jacket"));
            Assert.IsTrue(profile.TryGetCorrection("LeftArm", "Jacket", out seen));
            Assert.AreSame(general, seen);
            Assert.IsNotNull(profile.GetSlotOverride("Jacket"), "the slot still has its right arm");
            Assert.IsTrue(profile.RemoveCorrection("RightArm", "Jacket"));
            Assert.IsNull(profile.GetSlotOverride("Jacket"), "a slot left with nothing of its own is dropped");
            Assert.IsFalse(profile.RemoveCorrection("RightArm", "Jacket"));

            profile.GetOrAddCorrection("Spine", "Jacket");
            profile.GetOrAddCorrection("Hips", "Jacket");
            Assert.AreEqual(2, profile.GetSlotOverride("Jacket").boneCorrections.Count);
            Assert.IsTrue(profile.RemoveSlotOverride("Jacket"));
            Assert.IsFalse(profile.RemoveSlotOverride("Jacket"));
            Assert.AreEqual(2, profile.boneCorrections.Count, "left and right arm, as before");
        }

        [Test]
        public void ProfileEffectorsReshapeTheConvertedSlotWhereItStands()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "LeftHand", BindPose = BindPoseFor(source, "LeftHand", SourceMeshSpace) } };
            source.TryGetAvatarPosition(H("LeftHand"), out Vector3 handS);
            target.TryGetAvatarPosition(H("LeftHand"), out Vector3 handT);
            Vector3[] points = { handS + new Vector3(0.01f, 0f, 0f), handS + new Vector3(0f, 0.02f, 0.01f), handS + new Vector3(0.3f, 0.3f, 0.3f) };
            var weights = new[] { new[] { (0, 1f) }, new[] { (0, 1f) }, new[] { (0, 1f) } };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);

            Bake(UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default), target, out Vector3[] plain, out _, out _, out _);
            Assert.That(Vector3.Distance(plain[0], handT), Is.LessThan(0.05f), "the first two vertices end up at the target's hand");
            Assert.That(Vector3.Distance(plain[2], handT), Is.GreaterThan(0.2f), "the third is far from it");

            // An effector at the target's hand, for the jacket only. It works on the slot where the
            // conversion has put it: after the bone corrections, in avatar space.
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            var effector = new UMARigEffector
            {
                boneName = "LeftHand", shape = UMARigEffectorShape.Sphere, radius = 0.1f, falloff = 0f,
                action = UMARigEffectorAction.Move, move = new Vector3(0f, 0.03f, 0f),
            };
            profile.GetEffectorList("Jacket", true).Add(effector);

            var options = new UMASlotRigConversionOptions { Profile = profile, ProfileSlotName = "Jacket" };
            var report = new UMASlotRigConversionReport();
            Bake(UMASlotRigConverter.Convert(original, source, target, options, report), target, out Vector3[] p, out _, out _, out _);
            AssertClose(plain[0] + effector.move, p[0], Tolerance, "inside the effector");
            AssertClose(plain[1] + effector.move, p[1], Tolerance, "inside the effector");
            AssertClose(plain[2], p[2], Tolerance, "outside the effector");
            Assert.AreEqual(2, report.EffectorMovedVertices);
            Assert.AreEqual(0.03f, report.EffectorMaxMove, 1e-5f);

            // Another slot does not have it; an effector for all slots reaches both.
            options.ProfileSlotName = "Shirt";
            report = new UMASlotRigConversionReport();
            Bake(UMASlotRigConverter.Convert(original, source, target, options, report), target, out p, out _, out _, out _);
            AssertClose(plain[0], p[0], Tolerance, "the shirt has no effector");
            Assert.AreEqual(0, report.EffectorMovedVertices);
            var general = new UMARigEffector
            {
                boneName = "LeftHand", shape = UMARigEffectorShape.Sphere, radius = 0.1f, falloff = 0f,
                action = UMARigEffectorAction.Move, move = new Vector3(0.02f, 0f, 0f),
            };
            profile.effectors.Add(general);
            Bake(UMASlotRigConverter.Convert(original, source, target, options), target, out p, out _, out _, out _);
            AssertClose(plain[0] + general.move, p[0], Tolerance, "the shirt gets the effector for all slots");
            options.ProfileSlotName = "Jacket";
            Bake(UMASlotRigConverter.Convert(original, source, target, options), target, out p, out _, out _, out _);
            AssertClose(plain[0] + general.move + effector.move, p[0], Tolerance, "the jacket gets both, the general one first");

            // An exact conversion changes no fit, so it runs no effectors either.
            Bake(UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact), target, out Vector3[] exact, out _, out _, out _);
            var exactWithProfile = new UMASlotRigConversionOptions { CorrectionMode = UMARigCorrectionMode.None, Profile = profile, ProfileSlotName = "Jacket" };
            Bake(UMASlotRigConverter.Convert(original, source, target, exactWithProfile), target, out p, out _, out _, out _);
            AssertClose(exact[0], p[0], Tolerance, "exact stays exact");

            // An effector that clears the body says so when no body was given.
            profile.GetEffectorList("Jacket").Add(new UMARigEffector { boneName = "LeftHand", action = UMARigEffectorAction.ClearBody });
            report = new UMASlotRigConversionReport();
            UMASlotRigConverter.Convert(original, source, target, options, report);
            Assert.That(report.Warnings, Has.Some.Contains("no body surface"));
        }

        // --- mirroring profile entries -----------------------------------------------------------

        private static Vector3 MirrorX(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>A rotation mirrored across the plane x = 0.</summary>
        private static Quaternion MirrorX(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        /// <summary>
        /// A rig whose left and right arms are mirror images in avatar space. The right hand's own
        /// axes are laid out differently from the mirrored left hand's (turned a quarter about its
        /// x axis), as rigs sometimes are, so a scale has to be carried to the matching axis.
        /// </summary>
        private static List<UMATransform> SymmetricBones()
        {
            Quaternion leftArm = Quaternion.Euler(10f, 0f, 30f);
            Quaternion leftHand = Quaternion.Euler(0f, 15f, 0f);
            return new List<UMATransform>
            {
                Bone("Position", "Global", Vector3.zero, Quaternion.identity),
                Bone("Hips", "Position", new Vector3(0f, 1.0f, 0f), Quaternion.identity),
                Bone("Spine", "Hips", new Vector3(0f, 0.2f, 0f), Quaternion.identity),
                Bone("LeftArm", "Spine", new Vector3(-0.2f, 0.1f, 0.02f), leftArm),
                Bone("LeftHand", "LeftArm", new Vector3(-0.3f, 0.01f, 0f), leftHand),
                Bone("RightArm", "Spine", new Vector3(0.2f, 0.1f, 0.02f), MirrorX(leftArm)),
                Bone("RightHand", "RightArm", new Vector3(0.3f, 0.01f, 0f), MirrorX(leftHand) * Quaternion.Euler(90f, 0f, 0f)),
            };
        }

        [Test]
        public void AMirroredEntryMovesTheOtherSideAsTheMirrorImage()
        {
            UMARigRestPose rig = UMARigRestPose.FromBones(SymmetricBones(), "Global", Matrix4x4.identity);
            string[] boneNames = { "LeftArm", "LeftHand", "RightArm", "RightHand" };
            var influences = new Influence[boneNames.Length];
            for (int i = 0; i < boneNames.Length; i++)
            {
                influences[i] = new Influence { Bone = boneNames[i], BindPose = BindPoseFor(rig, boneNames[i], Matrix4x4.identity) };
            }
            rig.TryGetAvatarPosition(H("LeftArm"), out Vector3 arm);
            rig.TryGetAvatarPosition(H("LeftHand"), out Vector3 hand);
            rig.TryGetAvatarPosition(H("RightHand"), out Vector3 rightHand);
            AssertClose(MirrorX(hand), rightHand, Tolerance, "the test rig must be symmetric");

            Vector3[] left =
            {
                arm + new Vector3(-0.1f, 0.03f, 0.02f), arm + new Vector3(-0.2f, -0.02f, 0.01f), hand + new Vector3(0.03f, 0.01f, 0.02f),
                hand + new Vector3(-0.03f, 0.01f, 0.02f), hand + new Vector3(0.01f, -0.02f, -0.01f), hand + new Vector3(-0.02f, 0f, 0.03f),
            };
            int n = left.Length;
            var points = new Vector3[n * 2];
            var weights = new (int, float)[n * 2][];
            for (int i = 0; i < n; i++)
            {
                points[i] = left[i];
                points[i + n] = MirrorX(left[i]);
                // Arm only, an arm and hand blend, then hand only; the right side uses the right bones.
                weights[i] = i < 2 ? new[] { (0, 1f) } : i == 2 ? new[] { (0, 0.5f), (1, 0.5f) } : new[] { (1, 1f) };
                weights[i + n] = i < 2 ? new[] { (2, 1f) } : i == 2 ? new[] { (2, 0.5f), (3, 0.5f) } : new[] { (3, 1f) };
            }
            UMAMeshData original = MakeMesh(rig, Matrix4x4.identity, influences, points, weights);

            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            UMARigBoneCorrection armEntry = profile.GetOrAddCorrection("LeftArm");
            armEntry.positionOffset = new Vector3(0.01f, 0.02f, -0.03f);
            armEntry.rotationOffsetEuler = new Vector3(10f, 20f, 30f);
            UMARigBoneCorrection handEntry = profile.GetOrAddCorrection("LeftHand");
            handEntry.scaleOffset = new Vector3(1.3f, 1f, 0.7f);
            handEntry.includeChildren = false;
            handEntry.mode = UMARigBoneCorrectionMode.TranslateOnly;

            UMARigBoneCorrection rightArm = profile.MirrorCorrection("LeftArm", rig);
            UMARigBoneCorrection rightHandEntry = profile.MirrorCorrection("LeftHand", rig);

            Assert.AreEqual("RightArm", rightArm.boneName);
            AssertClose(new Vector3(-0.01f, 0.02f, -0.03f), rightArm.positionOffset, 1e-7f, "position offset flips x");
            AssertClose(new Vector3(10f, -20f, -30f), rightArm.rotationOffsetEuler, 1e-5f, "rotation turns the other way about y and z");
            Assert.IsTrue(rightArm.includeChildren);
            Assert.AreEqual("RightHand", rightHandEntry.boneName);
            Assert.IsFalse(rightHandEntry.includeChildren, "carry to children is copied");
            Assert.AreEqual(UMARigBoneCorrectionMode.TranslateOnly, rightHandEntry.mode, "mode is copied");
            // The right hand's y and z axes are swapped against the mirrored left hand's.
            AssertClose(new Vector3(1.3f, 0.7f, 1f), rightHandEntry.scaleOffset, 1e-6f, "scale follows the matching axis");

            // What counts: converted with the mirrored profile, the right side is the mirror image of the left.
            UMAMeshData converted = UMASlotRigConverter.Convert(original, rig, rig, new UMASlotRigConversionOptions { Profile = profile });
            Bake(converted, rig, out Vector3[] p, out Vector3[] normals, out _, out _);
            for (int i = 0; i < n; i++)
            {
                AssertClose(MirrorX(p[i]), p[i + n], Tolerance, "mirrored position " + i);
                AssertClose(MirrorX(normals[i]), normals[i + n], Tolerance * 10f, "mirrored normal " + i);
            }
            Assert.That(Vector3.Distance(points[0], p[0]), Is.GreaterThan(0.01f), "the entries must actually move the arm");
            Assert.That(Vector3.Distance(points[4], p[4]), Is.GreaterThan(0.005f), "the entries must actually move the hand");
        }

        [Test]
        public void OppositeBonesAreFoundByNameAndAWholeSideCanBeMirrored()
        {
            Assert.AreEqual("RightArmAdjust", UMASlotRigConversionProfile.GetOppositeBoneName("LeftArmAdjust"));
            Assert.AreEqual("LeftFoot", UMASlotRigConversionProfile.GetOppositeBoneName("RightFoot"));
            Assert.AreEqual("Hand_R", UMASlotRigConversionProfile.GetOppositeBoneName("Hand_L"));
            Assert.AreEqual("Hand_L", UMASlotRigConversionProfile.GetOppositeBoneName("Hand_R"));
            Assert.IsNull(UMASlotRigConversionProfile.GetOppositeBoneName("Hips"));
            Assert.IsNull(UMASlotRigConversionProfile.GetOppositeBoneName(""));
            Assert.IsNull(UMASlotRigConversionProfile.GetOppositeBoneName(null));
            Assert.IsTrue(UMASlotRigConversionProfile.IsLeftBoneName("LeftHand"));
            Assert.IsFalse(UMASlotRigConversionProfile.IsLeftBoneName("RightHand"));
            Assert.IsFalse(UMASlotRigConversionProfile.IsLeftBoneName("Hips"));

            UMARigRestPose rig = UMARigRestPose.FromBones(SymmetricBones(), "Global", Matrix4x4.identity);
            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            created.Add(profile);
            profile.GetOrAddCorrection("LeftArm").positionOffset = new Vector3(0.02f, 0f, 0f);
            profile.GetOrAddCorrection("LeftHand").rotationOffsetEuler = new Vector3(0f, 5f, 0f);
            profile.GetOrAddCorrection("RightArm").positionOffset = new Vector3(0.5f, 0.5f, 0.5f);
            profile.GetOrAddCorrection("Hips").positionOffset = new Vector3(0f, 0.01f, 0f);

            // Left onto right: both left entries are mirrored, and the right arm's own entry is replaced.
            Assert.AreEqual(2, profile.MirrorSide(true, rig));
            Assert.IsTrue(profile.TryGetCorrection("RightArm", out UMARigBoneCorrection rightArm));
            AssertClose(new Vector3(-0.02f, 0f, 0f), rightArm.positionOffset, 1e-7f, "right arm");
            Assert.IsTrue(profile.TryGetCorrection("RightHand", out UMARigBoneCorrection rightHand));
            AssertClose(new Vector3(0f, -5f, 0f), rightHand.rotationOffsetEuler, 1e-6f, "right hand");
            Assert.IsTrue(profile.TryGetCorrection("Hips", out UMARigBoneCorrection hips));
            AssertClose(new Vector3(0f, 0.01f, 0f), hips.positionOffset, 1e-7f, "a centre bone is left alone");
            Assert.AreEqual(5, profile.boneCorrections.Count);

            // And back the other way.
            rightArm.positionOffset = new Vector3(-0.04f, 0f, 0f);
            Assert.AreEqual(2, profile.MirrorSide(false, rig));
            Assert.IsTrue(profile.TryGetCorrection("LeftArm", out UMARigBoneCorrection leftArm));
            AssertClose(new Vector3(0.04f, 0f, 0f), leftArm.positionOffset, 1e-7f, "left arm");

            // Nothing to mirror: a centre bone, a bone with no entry, and a bone whose opposite the rig lacks.
            Assert.IsNull(profile.MirrorCorrection("Hips", rig));
            Assert.IsNull(profile.MirrorCorrection("LeftFoot", rig));
            profile.GetOrAddCorrection("LeftTail");
            Assert.IsNull(profile.MirrorCorrection("LeftTail", rig));
            Assert.IsFalse(profile.TryGetCorrection("RightTail", out _), "no entry is made for a bone the rig does not have");
            // Without a rig the names alone decide, and scale is copied as it is.
            profile.GetOrAddCorrection("LeftTail").scaleOffset = new Vector3(2f, 3f, 4f);
            UMARigBoneCorrection rightTail = profile.MirrorCorrection("LeftTail", null);
            Assert.IsNotNull(rightTail);
            AssertClose(new Vector3(2f, 3f, 4f), rightTail.scaleOffset, 1e-7f, "scale without a rig");
        }

        // --- legacy rig detection -----------------------------------------------------------------

        [Test]
        public void ARestPoseAssetDescribesTheSameRigItWasBakedFrom()
        {
            UMARigRestPose source = SourceRig();
            var asset = ScriptableObject.CreateInstance<UMARigRestPoseAsset>();
            created.Add(asset);
            asset.SetFrom(source, "Source");

            UMARigRestPose restored = asset.GetRestPose();

            Assert.AreEqual("Source", asset.sourceRaceName);
            Assert.AreEqual(source.BoneCount, asset.bones.Length);
            Assert.AreEqual(source.RootBoneName, restored.RootBoneName);
            Assert.AreSame(restored, asset.GetRestPose(), "the rest pose is built once");
            foreach (KeyValuePair<int, UMATransform> entry in source.Bones)
            {
                Assert.IsTrue(restored.TryGetAvatarMatrix(entry.Key, out Matrix4x4 restoredMatrix), entry.Value.name);
                source.TryGetAvatarMatrix(entry.Key, out Matrix4x4 sourceMatrix);
                Assert.That(UMARigRestPose.MaxAbsDifference(sourceMatrix, restoredMatrix), Is.LessThan(Tolerance), entry.Value.name);
                Assert.IsFalse(restored.Bones.TryGetValue(entry.Key, out UMATransform copy) && ReferenceEquals(copy, entry.Value), "the asset must hold its own bone definitions");
            }
        }

        [Test]
        public void ASlotsOwnBoneTableSaysWhichRigItWasBuiltFor()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            var influences = new[] { new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) } };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.0f, 0.05f), 0.2f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 1f) };
            UMAMeshData onSource = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            UMAMeshData onTarget = UMASlotRigConverter.Convert(onSource, source, target, UMASlotRigConversionOptions.Default);

            Assert.AreEqual(1f, UMALegacySlotConverter.GetBoneMatch(onSource, source, out int compared), 1e-6f);
            Assert.AreEqual(onSource.umaBones.Length, compared);
            Assert.That(UMALegacySlotConverter.GetBoneMatch(onSource, target, out _), Is.LessThan(UMALegacySlotConverter.OwnRigMatch));
            Assert.IsTrue(UMALegacySlotConverter.IsBuiltForLegacyRig(onSource, source, target), "built for the source rig");
            Assert.IsFalse(UMALegacySlotConverter.IsBuiltForLegacyRig(onSource, target, source), "not built for the target rig");
            // Once converted, the slot's bones are the target's: it is no longer a legacy slot for that pair.
            Assert.IsFalse(UMALegacySlotConverter.IsBuiltForLegacyRig(onTarget, source, target), "a converted slot must not be converted again");
            Assert.AreEqual(1f, UMALegacySlotConverter.GetBoneMatch(onTarget, target, out _), 1e-6f);

            // No bones, no verdict; and a race with no legacy rig converts nothing.
            UMAMeshData boneless = MakeMesh(source, SourceMeshSpace, influences, points, weights, slotBones: new UMATransform[0]);
            Assert.IsFalse(UMALegacySlotConverter.IsBuiltForLegacyRig(boneless, source, target));
            Assert.AreEqual(0, UMALegacySlotConverter.ConvertLegacySlots(new SlotData[1], null));
        }

        // --- mesh space ---------------------------------------------------------------------------

        [Test]
        public void CanonicalMeshSpaceIsTheOneMostDonorBonesShare()
        {
            UMARigRestPose source = SourceRig();
            UMARigRestPose target = TargetRig();
            // The first donor bone is bound half a metre away from where the other four agree.
            target.DonorMeshes[0].bindPoses[0] = target.DonorMeshes[0].bindPoses[0] * Matrix4x4.Translate(new Vector3(0.5f, 0f, 0f));

            Matrix4x4 canonical = target.GetCanonicalMeshToAvatar(1e-4f, out int mismatches, out float maxMismatch);

            Assert.That(UMARigRestPose.MaxAbsDifference(canonical, TargetMeshSpace), Is.LessThan(Tolerance), "the odd bone must not decide the mesh space");
            Assert.AreEqual(1, mismatches);
            Assert.That(maxMismatch, Is.GreaterThan(0.1f));

            var influences = new[]
            {
                new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) },
                new Influence { Bone = "Spine", BindPose = BindPoseFor(source, "Spine", SourceMeshSpace) },
            };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.15f, 0f), 0.1f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 0.6f), (1, 0.4f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            var report = new UMASlotRigConversionReport();
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Exact, report);

            Assert.AreEqual(1, report.TargetMeshSpaceMismatches);
            AssertSamePlace(original, source, converted, target, Tolerance);
            // Converted vertices sit in the shared mesh space, not the odd bone's.
            Matrix4x4 avatarToMesh = TargetMeshSpace.inverse;
            Bake(original, source, out Vector3[] avatarPositions, out _, out _, out _);
            for (int v = 0; v < points.Length; v++)
            {
                AssertClose(avatarToMesh.MultiplyPoint3x4(avatarPositions[v]), converted.vertices[v], Tolerance, "mesh-space vertex " + v);
            }
        }

        [Test]
        public void ASlotIsRecognisedAsBoundToItsOwnRigOnly()
        {
            UMARigRestPose source = SourceRig();
            // Give the target a mesh space the source does not share, as the UMA 2 and UMA 3 rigs have.
            UMARigRestPose target = MakeRig(TargetBones(), Matrix4x4.identity, Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f)), "Hips", "Spine", "Head", "LeftArm", "LeftHand");
            var influences = new[]
            {
                new Influence { Bone = "Hips", BindPose = BindPoseFor(source, "Hips", SourceMeshSpace) },
                new Influence { Bone = "Spine", BindPose = BindPoseFor(source, "Spine", SourceMeshSpace) },
            };
            Vector3[] points = Cloud(6, new Vector3(0f, 1.15f, 0f), 0.1f);
            var weights = new (int, float)[points.Length][];
            for (int v = 0; v < points.Length; v++) weights[v] = new[] { (0, 0.6f), (1, 0.4f) };
            UMAMeshData original = MakeMesh(source, SourceMeshSpace, influences, points, weights);
            UMAMeshData converted = UMASlotRigConverter.Convert(original, source, target, UMASlotRigConversionOptions.Default);

            Assert.IsTrue(UMASlotRigConverter.IsBoundToRigMeshSpace(original, source, 1e-4f, out float onSource));
            Assert.That(onSource, Is.LessThan(1e-4f));
            Assert.IsFalse(UMASlotRigConverter.IsBoundToRigMeshSpace(original, target, 1e-4f, out float onTarget), "same bone names, another rig's bind poses");
            Assert.That(onTarget, Is.GreaterThan(0.01f));
            Assert.IsTrue(UMASlotRigConverter.IsBoundToRigMeshSpace(converted, target, 1e-4f, out _), "a converted slot is native to the target");
            Assert.IsFalse(UMASlotRigConverter.IsBoundToRigMeshSpace(null, target, 1e-4f, out _));
        }
    }
}
