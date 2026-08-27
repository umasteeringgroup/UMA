#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEngine;

namespace UMA.Dismemberment.Tests
{
    public sealed class DismembermentMeshBuilderTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void BuilderUsesEveryModernInfluenceAndCreatesOpposingClosedCaps()
        {
            Mesh source = Own(CreateWeightedTetrahedron(true));
            int[] originalTriangles = source.triangles;
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f);
            bool[] mask = { false, false, false, false, true };

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source, mask,
                options, out DismembermentMeshBuildResult result, out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.EqualTo(1));
            Assert.That(result.capSubmeshIndex, Is.EqualTo(1));
            Assert.That(result.detachedMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.outerMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.detachedMesh.GetTriangles(0), Has.Length.EqualTo(9));
            Assert.That(result.outerMesh.GetTriangles(0), Has.Length.EqualTo(3));
            Assert.That(result.detachedMesh.GetTriangles(1), Has.Length.EqualTo(3));
            Assert.That(result.outerMesh.GetTriangles(1), Has.Length.EqualTo(3));
            Assert.That(result.detachedMesh.vertexCount, Is.EqualTo(source.vertexCount + 3));
            Assert.That(result.outerMesh.vertexCount, Is.EqualTo(source.vertexCount + 3));
            Assert.That(Vector3.Dot(result.detachedMesh.normals[source.vertexCount],
                result.outerMesh.normals[source.vertexCount]), Is.LessThan(-0.99f));
            Assert.That(source.subMeshCount, Is.EqualTo(1));
            Assert.That(source.triangles, Is.EqualTo(originalTriangles),
                "The UMA-owned source mesh must remain byte-for-byte topologically unchanged.");
            Assert.That(result.detachedMesh.blendShapeCount, Is.EqualTo(1));
            Assert.That(result.outerMesh.blendShapeCount, Is.EqualTo(1));
            AssertModernWeightsAreConsistent(result.detachedMesh);
            AssertModernWeightsAreConsistent(result.outerMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void ExistingCapSubmeshIsReusedInsteadOfAppended()
        {
            Mesh source = Own(CreateWeightedTetrahedron(false));
            var firstOptions = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f);
            DismembermentMeshBuilder.Build(source, new[] { false, true }, firstOptions,
                out DismembermentMeshBuildResult first, out string firstError);
            Assert.That(first, Is.Not.Null, firstError);
            var secondOptions = new DismembermentMeshBuildOptions(0.5f, 1, true, true, 0.25f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(first.outerMesh,
                new[] { true, false }, secondOptions, out DismembermentMeshBuildResult second,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(second.capSubmeshIndex, Is.EqualTo(1));
            Assert.That(second.detachedMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(second.outerMesh.subMeshCount, Is.EqualTo(2));
            second.DestroyMeshes();
            first.DestroyMeshes();
        }

        [Test]
        public void StrictCapsRejectAnOpenBoundaryWithoutMutatingSource()
        {
            Mesh source = Own(CreateOpenQuad());
            int[] original = source.triangles;
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 1f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.InvalidSource));
            Assert.That(result, Is.Null);
            Assert.That(error, Does.Contain("non-manifold"));
            Assert.That(source.triangles, Is.EqualTo(original));
        }

        [Test]
        public void ComponentSlicesEveryAffectedRendererAndRestoresBeforeRebuild()
        {
            GameObject avatarObject = Own(new GameObject("Dismemberment Test Avatar"));
            DynamicCharacterAvatar avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
            Transform root = CreateChild(avatarObject.transform, "Root");
            Transform global = CreateChild(root, "Global");
            Transform[] bones = CreateFiveBones(global);
            avatar.umaRoot = root.gameObject;
            avatar.skeleton = new UMASkeleton(global);
            SkinnedMeshRenderer first = CreateRenderer(avatarObject.transform, "Body", global,
                bones, Own(CreateWeightedTetrahedron(true)));
            SkinnedMeshRenderer second = CreateRenderer(avatarObject.transform, "Clothing", global,
                bones, Own(CreateWeightedTetrahedron(true)));
            Bounds firstBounds = new Bounds(new Vector3(0.25f, 0.5f, -0.25f),
                new Vector3(4f, 5f, 6f));
            first.localBounds = firstBounds;
            first.updateWhenOffscreen = true;
            first.SetBlendShapeWeight(0, 37f);
            int propertyId = Shader.PropertyToID("_DismembermentStateTest");
            var sourceBlock = new MaterialPropertyBlock();
            sourceBlock.SetFloat(propertyId, 0.75f);
            first.SetPropertyBlock(sourceBlock);
            avatar.SetRenderers(new[] { first, second });
            Mesh originalFirst = first.sharedMesh;
            Mesh originalSecond = second.sharedMesh;
            UmaDismemberment component = avatarObject.AddComponent<UmaDismemberment>();
            component.generateCaps = false;
            component.enabled = false;
            component.enabled = true;

            bool sliced = component.TrySlice(bones[4], 0.5f,
                out UmaDismemberment.DismemberedInfo info, out string failure);

            Assert.That(sliced, Is.True, failure);
            Assert.That(info.sourceRenderers, Has.Length.EqualTo(2));
            Assert.That(info.detachedRenderers, Has.Length.EqualTo(2));
            Assert.That(first.sharedMesh, Is.Not.SameAs(originalFirst));
            Assert.That(second.sharedMesh, Is.Not.SameAs(originalSecond));
            Assert.That(originalFirst.subMeshCount, Is.EqualTo(1));
            Assert.That(originalSecond.subMeshCount, Is.EqualTo(1));
            Assert.That(first.localBounds, Is.EqualTo(firstBounds));
            Assert.That(info.detachedRenderers[0].localBounds, Is.EqualTo(firstBounds));
            Assert.That(info.detachedRenderers[0].updateWhenOffscreen, Is.True);
            Assert.That(info.detachedRenderers[0].GetBlendShapeWeight(0), Is.EqualTo(37f));
            var detachedBlock = new MaterialPropertyBlock();
            info.detachedRenderers[0].GetPropertyBlock(detachedBlock);
            Assert.That(detachedBlock.GetFloat(propertyId), Is.EqualTo(0.75f));

            avatar.CharacterBegun.Invoke(avatar);
            Assert.That(first.sharedMesh, Is.SameAs(originalFirst));
            Assert.That(second.sharedMesh, Is.SameAs(originalSecond));
            Assert.That(first.localBounds, Is.EqualTo(firstBounds));
            Assert.That(info.root == null, Is.True,
                "The default rebuild policy must destroy detached pieces.");
        }

        private Mesh CreateWeightedTetrahedron(bool useFifthInfluence)
        {
            var mesh = new Mesh { name = "Weighted Tetrahedron" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(-1f, 0f, -1f),
                new Vector3(1f, 0f, -1f),
                new Vector3(0f, 0f, 1f)
            };
            mesh.normals = new[] { Vector3.up, Vector3.down, Vector3.down, Vector3.down };
            mesh.tangents = new[]
            {
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f)
            };
            mesh.uv = new[] { new Vector2(0.5f, 1f), Vector2.zero, Vector2.right, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 3, 1, 2, 3 };
            int boneCount = useFifthInfluence ? 5 : 2;
            mesh.bindposes = CreateIdentityBindposes(boneCount);
            SetWeights(mesh, useFifthInfluence);
            Vector3[] delta = { Vector3.up * 0.1f, Vector3.zero, Vector3.zero, Vector3.zero };
            mesh.AddBlendShapeFrame("TestShape", 100f, delta, new Vector3[4], new Vector3[4]);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateOpenQuad()
        {
            var mesh = new Mesh { name = "Open Quad" };
            mesh.vertices = new[]
            {
                Vector3.zero, Vector3.right, Vector3.one, Vector3.up
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bindposes = CreateIdentityBindposes(2);
            var counts = new NativeArray<byte>(new byte[] { 1, 1, 1, 1 }, Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(new[]
            {
                new BoneWeight1 { boneIndex = 0, weight = 1f },
                new BoneWeight1 { boneIndex = 1, weight = 1f },
                new BoneWeight1 { boneIndex = 0, weight = 1f },
                new BoneWeight1 { boneIndex = 0, weight = 1f }
            }, Allocator.Temp);
            mesh.SetBoneWeights(counts, weights);
            counts.Dispose();
            weights.Dispose();
            return mesh;
        }

        private static void SetWeights(Mesh mesh, bool useFifthInfluence)
        {
            byte apexCount = useFifthInfluence ? (byte)5 : (byte)1;
            var counts = new NativeArray<byte>(new[] { apexCount, (byte)1, (byte)1, (byte)1 },
                Allocator.Temp);
            var values = new List<BoneWeight1>();
            if (useFifthInfluence)
            {
                for (int i = 0; i < 4; i++)
                    values.Add(new BoneWeight1 { boneIndex = i, weight = 0.1f });
                values.Add(new BoneWeight1 { boneIndex = 4, weight = 0.6f });
            }
            else values.Add(new BoneWeight1 { boneIndex = 1, weight = 1f });
            for (int i = 0; i < 3; i++)
                values.Add(new BoneWeight1 { boneIndex = 0, weight = 1f });
            var weights = new NativeArray<BoneWeight1>(values.ToArray(), Allocator.Temp);
            mesh.SetBoneWeights(counts, weights);
            counts.Dispose();
            weights.Dispose();
        }

        private static void AssertModernWeightsAreConsistent(Mesh mesh)
        {
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            int total = 0;
            for (int i = 0; i < counts.Length; i++) total += counts[i];
            Assert.That(total, Is.EqualTo(weights.Length));
            counts.Dispose();
            weights.Dispose();
        }

        private static Matrix4x4[] CreateIdentityBindposes(int count)
        {
            var bindposes = new Matrix4x4[count];
            for (int i = 0; i < count; i++) bindposes[i] = Matrix4x4.identity;
            return bindposes;
        }

        private static Transform[] CreateFiveBones(Transform global)
        {
            var bones = new Transform[5];
            for (int i = 0; i < bones.Length; i++) bones[i] = CreateChild(global, "Bone" + i);
            return bones;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static SkinnedMeshRenderer CreateRenderer(Transform parent, string name,
            Transform rootBone, Transform[] bones, Mesh mesh)
        {
            GameObject rendererObject = new GameObject(name);
            rendererObject.transform.SetParent(parent, false);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = rootBone;
            renderer.bones = bones;
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            owned.Add(value);
            return value;
        }
    }
}
#endif
