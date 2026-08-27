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
            var options = new DismembermentMeshBuildOptions(0.15f, -1, true, true, 0.25f);
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
        public void BuilderWeldsDuplicatedSeamVerticesAndCreatesClosedCaps()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0.00005f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f,
                0.0001f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.EqualTo(1));
            Assert.That(result.capTriangleCount, Is.EqualTo(1));
            Assert.That(result.capSubmeshIndex, Is.EqualTo(1));
            Assert.That(result.detachedMesh.GetTriangles(1), Has.Length.EqualTo(3));
            Assert.That(result.outerMesh.GetTriangles(1), Has.Length.EqualTo(3));
            Assert.That(result.detachedMesh.vertexCount, Is.EqualTo(source.vertexCount + 3));
            Assert.That(result.outerMesh.vertexCount, Is.EqualTo(source.vertexCount + 3));
            Assert.That(Vector3.Dot(result.detachedMesh.normals[source.vertexCount],
                result.outerMesh.normals[source.vertexCount]), Is.LessThan(-0.99f));
            AssertVertexUsesBone(result.detachedMesh, source.vertexCount, 1);
            AssertVertexUsesBone(result.outerMesh, source.vertexCount, 0);
            AssertModernWeightsAreConsistent(result.detachedMesh);
            AssertModernWeightsAreConsistent(result.outerMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void DefaultCapUvModePreservesMeterScaledTiling()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0f));
            var quarterMeterOptions = new DismembermentMeshBuildOptions(0.5f, -1, true, true,
                0.25f, 0.0001f);
            var halfMeterOptions = new DismembermentMeshBuildOptions(0.5f, -1, true, true,
                0.5f, 0.0001f);

            DismembermentMeshBuilder.Build(source, new[] { false, true }, quarterMeterOptions,
                out DismembermentMeshBuildResult quarterMeter, out string quarterMeterError);
            DismembermentMeshBuilder.Build(source, new[] { false, true }, halfMeterOptions,
                out DismembermentMeshBuildResult halfMeter, out string halfMeterError);

            Assert.That(quarterMeter, Is.Not.Null, quarterMeterError);
            Assert.That(halfMeter, Is.Not.Null, halfMeterError);
            Vector2[] quarterMeterUvs = quarterMeter.detachedMesh.uv;
            Vector2[] halfMeterUvs = halfMeter.detachedMesh.uv;
            for (int vertex = source.vertexCount; vertex < quarterMeter.detachedMesh.vertexCount;
                vertex++)
            {
                Assert.That(Vector2.Distance(quarterMeterUvs[vertex],
                    halfMeterUvs[vertex] * 2f), Is.LessThan(0.000001f));
            }
            quarterMeter.DestroyMeshes();
            halfMeter.DestroyMeshes();
        }

        [Test]
        public void CenteredCapUvModeFitsEachSideInsideThePaddedUnitSquare()
        {
            const float padding = 0.02f;
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f,
                0.0001f, DismembermentCapUvMode.CenteredFit, padding);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            AssertCenteredCapUvs(result.detachedMesh, source.vertexCount, padding);
            AssertCenteredCapUvs(result.outerMesh, source.vertexCount, padding);
            result.DestroyMeshes();
        }

        [Test]
        public void NewBoneSettingsDefaultToLegacyCapUvMapping()
        {
            UmaDismemberment.BoneInfo settings = UmaDismemberment.BoneInfo.CreateDefault(
                HumanBodyBones.LeftLowerArm);

            Assert.That(settings.capUvMode,
                Is.EqualTo(DismembermentCapUvMode.MeterScaledTiled));
            Assert.That(settings.centeredCapUvPadding,
                Is.EqualTo(UmaDismemberment.DefaultCenteredCapUvPadding));
        }

        [Test]
        public void FullyAffectedArmorDetachesWithoutRequiringACutBoundary()
        {
            Mesh source = Own(CreateRigidlyWeightedTriangle(1));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f,
                0.0001f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.Zero);
            Assert.That(result.capTriangleCount, Is.Zero);
            Assert.That(result.capSubmeshIndex, Is.EqualTo(-1));
            Assert.That(result.detachedMesh.triangles, Has.Length.EqualTo(3));
            Assert.That(result.outerMesh.triangles, Is.Empty);
            AssertModernWeightsAreConsistent(result.detachedMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void BuilderCreatesIndependentCapsForBodyAndArmorShells()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(2, 0f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f,
                0.0001f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.EqualTo(2));
            Assert.That(result.capTriangleCount, Is.EqualTo(2));
            Assert.That(result.detachedMesh.GetTriangles(1), Has.Length.EqualTo(6));
            Assert.That(result.outerMesh.GetTriangles(1), Has.Length.EqualTo(6));
            AssertModernWeightsAreConsistent(result.detachedMesh);
            AssertModernWeightsAreConsistent(result.outerMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void StrictCapsRejectUnmatchedSeamsInsteadOfLeavingAHole()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0.001f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f,
                0.0001f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.InvalidSource));
            Assert.That(result, Is.Null);
            Assert.That(error, Does.Contain("No geometric cut boundary"));
        }

        [Test]
        public void NonStrictCapsCanKeepAnIntentionallyUnmatchedSeamOpen()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0.001f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, false, 0.25f,
                0.0001f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.Zero);
            Assert.That(result.capTriangleCount, Is.Zero);
            Assert.That(result.capSubmeshIndex, Is.EqualTo(-1));
            Assert.That(result.detachedMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(result.outerMesh.subMeshCount, Is.EqualTo(1));
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
            first.updateWhenOffscreen = false;
            second.updateWhenOffscreen = true;
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

            bool sliced = component.TrySlice(bones[4], 0.15f,
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
            Assert.That(info.detachedRenderers[0].updateWhenOffscreen, Is.False);
            Assert.That(info.detachedRenderers[1].updateWhenOffscreen, Is.True);
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

        [Test]
        public void ComponentCapsBodyAndArmorRenderersWithTheConfiguredMaterial()
        {
            Shader capShader = Shader.Find("UMA/Dismemberment/Cap Unlit");
            Assert.That(capShader, Is.Not.Null, "The sample cap shader must be importable.");
            Material capMaterial = Own(new Material(capShader));
            GameObject avatarObject = Own(new GameObject("Capped Dismemberment Test Avatar"));
            DynamicCharacterAvatar avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
            Transform root = CreateChild(avatarObject.transform, "Root");
            Transform global = CreateChild(root, "Global");
            Transform[] bones = CreateFiveBones(global);
            avatar.umaRoot = root.gameObject;
            avatar.skeleton = new UMASkeleton(global);
            SkinnedMeshRenderer body = CreateRenderer(avatarObject.transform, "Body", global,
                bones, Own(CreateDuplicatedSeamShells(1, 0f)));
            SkinnedMeshRenderer armor = CreateRenderer(avatarObject.transform, "Armor", global,
                bones, Own(CreateDuplicatedSeamShells(1, 0f)));
            avatar.SetRenderers(new[] { body, armor });
            UmaDismemberment component = avatarObject.AddComponent<UmaDismemberment>();
            component.sliceFill = capMaterial;
            component.generateCaps = true;
            component.requireClosedCaps = true;
            component.seamWeldTolerance = 0.0001f;
            component.enabled = false;
            component.enabled = true;

            bool sliced = component.TrySlice(bones[1], 0.5f,
                out UmaDismemberment.DismemberedInfo info, out string failure);

            Assert.That(sliced, Is.True, failure);
            Assert.That(info.sourceRenderers, Has.Length.EqualTo(2));
            Assert.That(info.detachedRenderers, Has.Length.EqualTo(2));
            for (int renderer = 0; renderer < 2; renderer++)
            {
                Assert.That(info.sourceRenderers[renderer].sharedMesh.subMeshCount, Is.EqualTo(2));
                Assert.That(info.detachedRenderers[renderer].sharedMesh.subMeshCount, Is.EqualTo(2));
                Assert.That(info.sourceRenderers[renderer].sharedMesh.GetTriangles(1),
                    Has.Length.EqualTo(3));
                Assert.That(info.detachedRenderers[renderer].sharedMesh.GetTriangles(1),
                    Has.Length.EqualTo(3));
                Assert.That(info.sourceRenderers[renderer].sharedMaterials[1],
                    Is.SameAs(capMaterial));
                Assert.That(info.detachedRenderers[renderer].sharedMaterials[1],
                    Is.SameAs(capMaterial));
            }
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

        private static Mesh CreateDuplicatedSeamShells(int shellCount, float outerSeamOffset)
        {
            const int verticesPerShell = 7;
            var vertices = new Vector3[shellCount * verticesPerShell];
            var triangles = new int[shellCount * 12];
            var counts = new NativeArray<byte>(new byte[vertices.Length], Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(new BoneWeight1[vertices.Length],
                Allocator.Temp);
            for (int shell = 0; shell < shellCount; shell++)
            {
                int vertex = shell * verticesPerShell;
                int triangle = shell * 12;
                Vector3 shellOffset = Vector3.right * shell * 4f;
                Vector3 seamOffset = Vector3.right * outerSeamOffset;
                vertices[vertex] = shellOffset + new Vector3(0f, 1f, 0f);
                vertices[vertex + 1] = shellOffset + new Vector3(-1f, 0f, -1f);
                vertices[vertex + 2] = shellOffset + new Vector3(1f, 0f, -1f);
                vertices[vertex + 3] = shellOffset + new Vector3(0f, 0f, 1f);
                vertices[vertex + 4] = vertices[vertex + 1] + seamOffset;
                vertices[vertex + 5] = vertices[vertex + 2] + seamOffset;
                vertices[vertex + 6] = vertices[vertex + 3] + seamOffset;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
                triangles[triangle + 6] = vertex;
                triangles[triangle + 7] = vertex + 1;
                triangles[triangle + 8] = vertex + 3;
                triangles[triangle + 9] = vertex + 4;
                triangles[triangle + 10] = vertex + 5;
                triangles[triangle + 11] = vertex + 6;
                for (int i = 0; i < verticesPerShell; i++)
                {
                    counts[vertex + i] = 1;
                    weights[vertex + i] = new BoneWeight1
                    {
                        boneIndex = i <= 3 ? 1 : 0,
                        weight = 1f
                    };
                }
            }

            var mesh = new Mesh { name = $"Duplicated Seam Shells ({shellCount})" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.bindposes = CreateIdentityBindposes(2);
            mesh.SetBoneWeights(counts, weights);
            counts.Dispose();
            weights.Dispose();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRigidlyWeightedTriangle(int boneIndex)
        {
            var mesh = new Mesh { name = "Rigidly Weighted Armor" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = CreateIdentityBindposes(boneIndex + 1);
            var counts = new NativeArray<byte>(new byte[] { 1, 1, 1 }, Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(new[]
            {
                new BoneWeight1 { boneIndex = boneIndex, weight = 1f },
                new BoneWeight1 { boneIndex = boneIndex, weight = 1f },
                new BoneWeight1 { boneIndex = boneIndex, weight = 1f }
            }, Allocator.Temp);
            mesh.SetBoneWeights(counts, weights);
            counts.Dispose();
            weights.Dispose();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
                    values.Add(new BoneWeight1 { boneIndex = i, weight = 0.21f });
                values.Add(new BoneWeight1 { boneIndex = 4, weight = 0.16f });
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
            try
            {
                int total = 0;
                for (int i = 0; i < counts.Length; i++) total += counts[i];
                Assert.That(total, Is.EqualTo(weights.Length));
            }
            finally
            {
                if (counts.IsCreated) counts.Dispose();
                if (weights.IsCreated) weights.Dispose();
            }
        }

        private static void AssertVertexUsesBone(Mesh mesh, int vertexIndex, int expectedBone)
        {
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            try
            {
                int offset = 0;
                for (int vertex = 0; vertex < vertexIndex; vertex++) offset += counts[vertex];
                Assert.That(counts[vertexIndex], Is.GreaterThan(0));
                bool found = false;
                for (int influence = 0; influence < counts[vertexIndex]; influence++)
                    if (weights[offset + influence].boneIndex == expectedBone) found = true;
                Assert.That(found, Is.True,
                    $"Vertex {vertexIndex} should retain a weight from bone {expectedBone}.");
            }
            finally
            {
                if (counts.IsCreated) counts.Dispose();
                if (weights.IsCreated) weights.Dispose();
            }
        }

        private static void AssertCenteredCapUvs(Mesh mesh, int firstCapVertex, float padding)
        {
            Vector2[] uvs = mesh.uv;
            Assert.That(mesh.vertexCount - firstCapVertex, Is.EqualTo(3));
            Vector2 vertexAverage = Vector2.zero;
            for (int vertex = firstCapVertex; vertex < mesh.vertexCount; vertex++)
            {
                Vector2 uv = uvs[vertex];
                Assert.That(uv.x, Is.InRange(padding - 0.000001f,
                    1f - padding + 0.000001f));
                Assert.That(uv.y, Is.InRange(padding - 0.000001f,
                    1f - padding + 0.000001f));
                vertexAverage += uv;
            }
            vertexAverage /= 3f;
            Assert.That(Vector2.Distance(vertexAverage, Vector2.one * 0.5f),
                Is.LessThan(0.000001f));
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
