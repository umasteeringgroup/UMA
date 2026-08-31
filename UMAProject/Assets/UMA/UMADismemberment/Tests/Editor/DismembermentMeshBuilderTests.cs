#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UMA.Dynamics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

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
        public void BuilderPreservesUmaMultistreamVertexLayoutAndSourceBytes()
        {
            Mesh source = Own(CreateUmaMultistreamTetrahedron());
            VertexAttributeDescriptor[] sourceLayout = source.GetVertexAttributes();
            int sourceVertexCount = source.vertexCount;
            var sourceStreamBytes = new List<byte[]>();
            int streamCount = GetStreamCount(sourceLayout);
            using (Mesh.MeshDataArray sourceData = Mesh.AcquireReadOnlyMeshData(source))
            {
                for (int stream = 0; stream < streamCount; stream++)
                    sourceStreamBytes.Add(sourceData[0].GetVertexData<byte>(stream).ToArray());
            }

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true },
                new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f),
                out DismembermentMeshBuildResult result, out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            AssertPreservedVertexLayout(source, result.outerMesh, sourceLayout);
            AssertPreservedVertexLayout(source, result.detachedMesh, sourceLayout);
            AssertPreservedSourceStreamBytes(result.outerMesh, sourceStreamBytes,
                sourceVertexCount, sourceLayout);
            AssertPreservedSourceStreamBytes(result.detachedMesh, sourceStreamBytes,
                sourceVertexCount, sourceLayout);
            result.DestroyMeshes();
        }

        [Test]
        public void BuilderPreservesUmaMultistreamVertexLayoutAcrossRepeatedCuts()
        {
            Mesh source = Own(CreateUmaMultistreamTetrahedron());
            VertexAttributeDescriptor[] sourceLayout = source.GetVertexAttributes();
            var firstOptions = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f);
            DismembermentMeshBuildStatus firstStatus = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, firstOptions, out DismembermentMeshBuildResult first,
                out string firstError);
            Assert.That(firstStatus, Is.EqualTo(DismembermentMeshBuildStatus.Success), firstError);
            AssertPreservedVertexLayout(source, first.outerMesh, sourceLayout);

            var secondOptions = new DismembermentMeshBuildOptions(0.5f,
                first.capSubmeshIndex, true, true, 0.25f);
            DismembermentMeshBuildStatus secondStatus = DismembermentMeshBuilder.Build(
                first.outerMesh, new[] { true, false }, secondOptions,
                out DismembermentMeshBuildResult second, out string secondError);

            Assert.That(secondStatus, Is.EqualTo(DismembermentMeshBuildStatus.Success),
                secondError);
            AssertPreservedVertexLayout(first.outerMesh, second.outerMesh, sourceLayout);
            AssertPreservedVertexLayout(first.outerMesh, second.detachedMesh, sourceLayout);
            second.DestroyMeshes();
            first.DestroyMeshes();
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
        public void BuilderRetainsDurableOrderedBoundaryUvData()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true, 0.25f,
                0.0001f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoops, Has.Length.EqualTo(1));
            DismembermentBoundaryLoopData loop = result.boundaryLoops[0];
            Assert.That(loop.sourceSubmeshIndex, Is.EqualTo(0));
            Assert.That(loop.sourceVertexIndices, Has.Length.EqualTo(3));
            Assert.That(loop.boundaryUV, Has.Length.EqualTo(3));
            Assert.That(loop.boundaryLocalPositions, Has.Length.EqualTo(3));
            for (int i = 0; i < loop.sourceVertexIndices.Length; i++)
            {
                int sourceIndex = loop.sourceVertexIndices[i];
                Assert.That(loop.boundaryUV[i], Is.EqualTo(source.uv[sourceIndex]));
                Assert.That(loop.boundaryLocalPositions[i],
                    Is.EqualTo(source.vertices[sourceIndex]));
            }

            Vector2[] durableUv = loop.boundaryUV;
            result.DestroyMeshes();
            Assert.That(durableUv, Has.Length.EqualTo(3),
                "Boundary data must not depend on the temporary output meshes.");
        }

        [Test]
        public void BuilderRetainsCutBoundaryWhenProceduralCapsAreDisabled()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, false, false, 0.25f,
                0.0001f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.capSubmeshIndex, Is.EqualTo(-1));
            Assert.That(result.capTriangleCount, Is.Zero);
            Assert.That(result.boundaryLoops, Has.Length.EqualTo(1),
                "Surface effects need the real cut loop even when visual caps are disabled.");
            Assert.That(result.outerMesh.subMeshCount, Is.EqualTo(source.subMeshCount));
            Assert.That(result.detachedMesh.subMeshCount, Is.EqualTo(source.subMeshCount));
            result.DestroyMeshes();
        }

        [Test]
        public void RuntimeDecalHandlesAreSessionScopedAndStable()
        {
            var first = new RuntimeDecalHandle(17, 1);
            var same = new RuntimeDecalHandle(17, 1);
            var next = new RuntimeDecalHandle(17, 2);
            var otherSession = new RuntimeDecalHandle(18, 1);

            Assert.That(first.IsValid, Is.True);
            Assert.That(first, Is.EqualTo(same));
            Assert.That(first, Is.Not.EqualTo(next));
            Assert.That(first, Is.Not.EqualTo(otherSession));
            Assert.That(default(RuntimeDecalHandle).IsValid, Is.False);
        }

        [Test]
        public void SurfaceFluidDefaultsUseBoundedMetricValues()
        {
            UMASurfaceFluidProfile profile = Own(
                ScriptableObject.CreateInstance<UMASurfaceFluidProfile>());

            Assert.That(profile.emissionRadiusMeters, Is.GreaterThan(0f));
            Assert.That(profile.maximumTravelMeters, Is.GreaterThan(0f));
            Assert.That(profile.trailDepositionPerMeter, Is.GreaterThan(0f));
            Assert.That(profile.breakupScaleMeters, Is.GreaterThan(0f));
            Assert.That(profile.simulationResolutionCap, Is.InRange(64, 1024));
            Assert.That(profile.channels, Is.EqualTo(SurfaceFluidChannels.Albedo));
            Assert.That(profile.detachedRoute,
                Is.EqualTo(SurfaceFluidDetachedRoute.SourceBody));
            Assert.That(profile.fallbackHoldingDuration, Is.GreaterThanOrEqualTo(0f));
            Assert.That(profile.fallbackFadeDuration, Is.GreaterThan(0f));
            Assert.That(profile.fallbackMaximumLifetime,
                Is.GreaterThan(profile.fallbackFadeDuration));
            Assert.That(profile.fallbackMaximumLifetime,
                Is.LessThan(profile.holdingDuration + profile.fadeDuration),
                "Temporary line geometry must not inherit the texture-fluid lifetime.");
        }

        [Test]
        public void SurfaceFluidFallbackTrailFollowsItsAnchorInsteadOfFloatingInWorld()
        {
            UMASurfaceFluidProfile profile = Own(
                ScriptableObject.CreateInstance<UMASurfaceFluidProfile>());
            GameObject anchor = Own(new GameObject("Fallback Anchor"));
            GameObject host = Own(new GameObject("Fallback Trail"));
            host.transform.SetParent(anchor.transform, false);
            UMASurfaceFluidFallbackTrail trail =
                host.AddComponent<UMASurfaceFluidFallbackTrail>();
            Vector3 origin = new Vector3(0.2f, 1.4f, -0.3f);

            trail.Initialize(origin, Vector3.forward, profile, null);

            LineRenderer line = host.GetComponent<LineRenderer>();
            Assert.That(line, Is.Not.Null);
            Assert.That(line.useWorldSpace, Is.False);
            Vector3 before = host.transform.TransformPoint(line.GetPosition(0));
            Vector3 movement = new Vector3(1.5f, -0.25f, 0.75f);
            anchor.transform.position += movement;
            Vector3 after = host.transform.TransformPoint(line.GetPosition(0));
            Assert.That(Vector3.Distance(after, before + movement), Is.LessThan(0.00001f),
                "The fallback must remain attached when its character or ragdoll moves.");
        }

        [Test]
        public void SurfaceFluidGpuResourcesArePackagedAndLoadable()
        {
            Assert.That(Resources.Load<ComputeShader>(
                "UMA/Dismemberment/SurfaceFluid"), Is.Not.Null);
            Assert.That(Resources.Load<Shader>(
                "UMA/Dismemberment/SurfaceField"), Is.Not.Null);
            Assert.That(Resources.Load<Shader>(
                "UMA/Dismemberment/RuntimeDecalComposite"), Is.Not.Null);
            Assert.That(Resources.Load<Shader>(
                "UMA/Dismemberment/SourceMask"), Is.Not.Null);
            Assert.That(Resources.Load<Shader>(
                "UMA/Dismemberment/FallbackTrail"), Is.Not.Null);
        }

        [Test]
        public void EmptyRuntimeSurfaceControllerOwnsNoActiveEffects()
        {
            GameObject avatarObject = Own(new GameObject("Empty Surface Decal Avatar"));
            avatarObject.AddComponent<DynamicCharacterAvatar>();
            UMARuntimeSurfaceDecalController controller =
                avatarObject.AddComponent<UMARuntimeSurfaceDecalController>();

            Assert.That(controller.ActiveEffectCount, Is.Zero);
            Assert.That(controller.GetDebugTexture(
                RuntimeSurfaceDebugTexture.CompositedOutput), Is.Null);

            controller.enabled = false;
            Assert.That(controller.ActiveEffectCount, Is.Zero);
        }

        [Test]
        public void MeshChangeDefersSurfaceRenderingBeforeAnyAtlasContextExists()
        {
            GameObject avatarObject = Own(new GameObject("Surface Mesh Change Avatar"));
            avatarObject.AddComponent<DynamicCharacterAvatar>();
            UMARuntimeSurfaceDecalController controller =
                avatarObject.AddComponent<UMARuntimeSurfaceDecalController>();
            SkinnedMeshRenderer renderer = avatarObject.AddComponent<SkinnedMeshRenderer>();

            controller.PrepareForRendererMeshChange(renderer);

            Assert.That(controller.IsRendererSurfaceRenderingDeferred(renderer), Is.True,
                "The renderer-level guard must exist before a cut callback creates its first " +
                "atlas context.");
        }

        [Test]
        public void StandaloneDecalBleedRejectsIncompleteRequestsWithoutAllocating()
        {
            GameObject avatarObject = Own(new GameObject("Standalone Decal Fluid Avatar"));
            avatarObject.AddComponent<DynamicCharacterAvatar>();
            UMARuntimeSurfaceDecalController controller =
                avatarObject.AddComponent<UMARuntimeSurfaceDecalController>();
            UMASurfaceFluidProfile profile = Own(
                ScriptableObject.CreateInstance<UMASurfaceFluidProfile>());

            RuntimeDecalHandle missingStamp = controller.StartBleedFromDecal(null, profile);
            RuntimeDecalHandle failedHit = controller.StartBleedFromDecal(null, profile,
                new DecalRenderTexture.DecalLayerResult { success = false });
            RuntimeDecalHandle missingPersistentStamp = controller.AddPersistentStamp(null);

            Assert.That(missingStamp.IsValid, Is.False);
            Assert.That(failedHit.IsValid, Is.False);
            Assert.That(missingPersistentStamp.IsValid, Is.False);
            Assert.That(controller.ActiveEffectCount, Is.Zero,
                "Rejected standalone emitters must not allocate an effect record.");
        }

        [Test]
        public void DetachedPieceMaterialOwnerReleasesIndependentClones()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            GameObject piece = Own(new GameObject("Independent Detached Material Owner"));
            DismemberedPieceMaterialOwner owner =
                piece.AddComponent<DismemberedPieceMaterialOwner>();
            Material clone = Own(new Material(shader));
            owner.Add(clone);

            UnityEngine.Object.DestroyImmediate(piece);

            Assert.That(clone == null, Is.True,
                "The detached root must own and release every generated material clone.");
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
            Assert.That(settings.physicsDefinitions, Is.Not.Null.And.Empty);
            Assert.That(settings.physicsMode, Is.EqualTo(DismemberedPhysicsMode.Automatic));
            Assert.That(settings.trimDetachedRig, Is.False);
            Assert.That(settings.ragdollMainBody, Is.False);
        }

        [Test]
        public void DetachedMeshRemovesCrossCutWeightsAndUsesCutBoneFallback()
        {
            Mesh source = Own(CreateWeightedTetrahedron(true));
            var options = new DismembermentMeshBuildOptions(0.15f, -1, true, true, 0.25f,
                DismembermentMeshBuildOptions.DefaultSeamWeldTolerance,
                DismembermentCapUvMode.MeterScaledTiled,
                UmaDismemberment.DefaultCenteredCapUvPadding, 4);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, false, false, false, true }, options,
                out DismembermentMeshBuildResult result, out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            AssertAllWeightsUseBone(result.detachedMesh, 4);
            AssertVertexUsesBone(result.outerMesh, 0, 0);
            result.DestroyMeshes();
        }

        [Test]
        public void DetachedBonePaletteCanBeCompactedAfterWeightSanitization()
        {
            Mesh source = Own(CreateWeightedTetrahedron(true));
            var options = new DismembermentMeshBuildOptions(0.15f, -1, true, true, 0.25f,
                DismembermentMeshBuildOptions.DefaultSeamWeldTolerance,
                DismembermentCapUvMode.MeterScaledTiled,
                UmaDismemberment.DefaultCenteredCapUvPadding, 4);
            DismembermentMeshBuilder.Build(source,
                new[] { false, false, false, false, true }, options,
                out DismembermentMeshBuildResult result, out string buildError);
            Assert.That(result, Is.Not.Null, buildError);
            Transform paletteRoot = Own(new GameObject("Palette Root")).transform;
            Transform[] bones = CreateFiveBones(paletteRoot);

            bool compacted = UmaDismemberment.TryCompactDetachedBonePalette(
                result.detachedMesh, bones, out Transform[] compactBones, out string error);

            Assert.That(compacted, Is.True, error);
            Assert.That(compactBones, Has.Length.EqualTo(1));
            Assert.That(compactBones[0], Is.SameAs(bones[4]));
            Assert.That(result.detachedMesh.bindposes, Has.Length.EqualTo(1));
            AssertAllWeightsUseBone(result.detachedMesh, 0);
            AssertModernWeightsAreConsistent(result.detachedMesh);

            bool compactedAgain = UmaDismemberment.TryCompactDetachedBonePalette(
                result.detachedMesh, compactBones, out Transform[] secondPalette,
                out string secondError);

            Assert.That(compactedAgain, Is.True, secondError);
            Assert.That(secondPalette, Is.SameAs(compactBones));
            Assert.That(result.detachedMesh.bindposes, Has.Length.EqualTo(1));
            AssertAllWeightsUseBone(result.detachedMesh, 0);
            AssertModernWeightsAreConsistent(result.detachedMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void DetachedRigTrimmingKeepsGlobalPathAndCompleteCutSubtree()
        {
            GameObject rig = Own(new GameObject("Detached Skeleton"));
            Transform global = CreateChild(rig.transform, "Global");
            Transform spine = CreateChild(global, "Spine");
            Transform shoulder = CreateChild(spine, "Shoulder");
            Transform arm = CreateChild(shoulder, "Arm");
            Transform forearm = CreateChild(arm, "ForeArm");
            Transform hand = CreateChild(forearm, "Hand");
            Transform oppositeArm = CreateChild(spine, "Opposite Arm");
            Transform leg = CreateChild(global, "Leg");

            UmaDismemberment.TrimDetachedHierarchy(global, arm);

            Assert.That(global, Is.Not.Null);
            Assert.That(spine, Is.Not.Null);
            Assert.That(shoulder, Is.Not.Null);
            Assert.That(arm, Is.Not.Null);
            Assert.That(forearm, Is.Not.Null);
            Assert.That(hand, Is.Not.Null);
            Assert.That(oppositeArm == null, Is.True);
            Assert.That(leg == null, Is.True);
        }

        [Test]
        public void ComponentReturnsPhysicsDefinitionsForTheRequestedCut()
        {
            GameObject avatarRoot = Own(new GameObject("Avatar"));
            UmaDismemberment component = avatarRoot.AddComponent<UmaDismemberment>();
            UMAPhysicsElement definition = Own(CreatePhysicsElement("Left Forearm",
                "LeftForeArm", "LeftArm", 1f));
            UmaDismemberment.BoneInfo configured = UmaDismemberment.BoneInfo.CreateDefault(
                HumanBodyBones.LeftLowerArm);
            configured.physicsDefinitions.Add(definition);
            configured.ragdollMainBody = true;
            component.sliceableHumanBones.Add(configured);

            bool found = component.TryGetBoneSettings(HumanBodyBones.LeftLowerArm,
                out UmaDismemberment.BoneInfo resolved);

            Assert.That(found, Is.True);
            Assert.That(resolved.physicsDefinitions, Has.Count.EqualTo(1));
            Assert.That(resolved.physicsDefinitions[0], Is.SameAs(definition));
            Assert.That(resolved.ragdollMainBody, Is.True);
            Assert.That(component.TryGetBoneSettings(HumanBodyBones.RightLowerArm,
                out _), Is.False);
        }

        [Test]
        public void MainBodyRagdollResolverFindsPhysicsAvatarOnCharacterHierarchy()
        {
            GameObject character = Own(new GameObject("Character"));
            UMAPhysicsAvatar physicsAvatar = character.AddComponent<UMAPhysicsAvatar>();
            Transform dismembermentObject = CreateChild(character.transform, "UMA Avatar");
            UmaDismemberment component = dismembermentObject.gameObject
                .AddComponent<UmaDismemberment>();

            UMAPhysicsAvatar resolved = UmaDismemberment.FindMainBodyPhysicsAvatar(component);

            Assert.That(resolved, Is.SameAs(physicsAvatar));
        }

        [Test]
        public void DetachedRagdollUsesUmaPhysicsDefinitionsForPartialRig()
        {
            GameObject rig = Own(new GameObject("Detached Rig"));
            Transform upper = CreateChild(rig.transform, "Upper");
            Transform lower = CreateChild(upper, "Lower");
            UMAPhysicsElement upperDefinition = Own(CreatePhysicsElement("Upper Definition",
                "Upper", "Body Bone Not In This Cut", 2f, new ColliderDefinition
                {
                    colliderType = ColliderDefinition.ColliderType.Sphere,
                    colliderCentre = new Vector3(0.1f, 0.2f, 0.3f),
                    sphereRadius = 0.25f
                }));
            UMAPhysicsElement lowerDefinition = Own(CreatePhysicsElement("Lower Definition",
                "Lower", "Upper", 1f, new ColliderDefinition
                {
                    colliderType = ColliderDefinition.ColliderType.Capsule,
                    colliderCentre = new Vector3(0f, 0.3f, 0f),
                    capsuleRadius = 0.1f,
                    capsuleHeight = 0.6f,
                    capsuleAlignment = ColliderDefinition.Direction.Z
                }));
            lowerDefinition.axis = Vector3.right;
            lowerDefinition.swingAxis = Vector3.forward;
            lowerDefinition.lowTwistLimit = -30f;
            lowerDefinition.highTwistLimit = 40f;
            lowerDefinition.swing1Limit = 50f;
            lowerDefinition.swing2Limit = 10f;
            lowerDefinition.enablePreprocessing = false;

            bool built = DismemberedRagdollBuilder.TryBuild(rig.transform,
                new[] { upperDefinition, lowerDefinition }, 8,
                out DismemberedRagdollBuildResult result, out string error);

            Assert.That(built, Is.True, error);
            Assert.That(result.rigidbodies, Has.Length.EqualTo(2));
            Assert.That(result.rootRigidbodies, Has.Length.EqualTo(1));
            Assert.That(result.rootRigidbodies[0].transform, Is.SameAs(upper));
            Assert.That(result.colliders, Has.Length.EqualTo(2));
            Assert.That(result.joints, Has.Length.EqualTo(1));
            Assert.That(upper.gameObject.layer, Is.EqualTo(8));
            Assert.That(lower.gameObject.layer, Is.EqualTo(8));
            Assert.That(upper.GetComponent<Rigidbody>().mass, Is.EqualTo(2f));
            Assert.That(lower.GetComponent<Rigidbody>().mass, Is.EqualTo(1f));
            Assert.That(upper.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(lower.GetComponent<Rigidbody>().isKinematic, Is.False);
            SphereCollider sphere = upper.GetComponent<SphereCollider>();
            Assert.That(sphere.center, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
            Assert.That(sphere.radius, Is.EqualTo(0.25f));
            CapsuleCollider capsule = lower.GetComponent<CapsuleCollider>();
            Assert.That(capsule.center, Is.EqualTo(new Vector3(0f, 0.3f, 0f)));
            Assert.That(capsule.radius, Is.EqualTo(0.1f));
            Assert.That(capsule.height, Is.EqualTo(0.6f));
            Assert.That(capsule.direction, Is.EqualTo(2));
            CharacterJoint joint = result.joints[0];
            Assert.That(joint.connectedBody, Is.SameAs(upper.GetComponent<Rigidbody>()));
            Assert.That(joint.axis, Is.EqualTo(Vector3.right));
            Assert.That(joint.swingAxis, Is.EqualTo(Vector3.forward));
            Assert.That(joint.lowTwistLimit.limit, Is.EqualTo(-30f));
            Assert.That(joint.highTwistLimit.limit, Is.EqualTo(40f));
            Assert.That(joint.swing1Limit.limit, Is.EqualTo(50f));
            Assert.That(joint.swing2Limit.limit, Is.EqualTo(10f));
            Assert.That(joint.enablePreprocessing, Is.False);
        }

        [Test]
        public void AutomaticPhysicsUsesRigidForOneDefinitionAndArticulatedForAChain()
        {
            UMAPhysicsElement first = Own(CreatePhysicsElement("First", "First",
                string.Empty, 1f));
            UMAPhysicsElement second = Own(CreatePhysicsElement("Second", "Second",
                "First", 1f));

            Assert.That(DismemberedRagdollBuilder.ResolvePhysicsMode(
                DismemberedPhysicsMode.Automatic, new[] { first, first, null }),
                Is.EqualTo(DismemberedPhysicsMode.Rigid));
            Assert.That(DismemberedRagdollBuilder.ResolvePhysicsMode(
                DismemberedPhysicsMode.Automatic, new[] { first, null, second }),
                Is.EqualTo(DismemberedPhysicsMode.ArticulatedRagdoll));
            Assert.That(DismemberedRagdollBuilder.ResolvePhysicsMode(
                DismemberedPhysicsMode.None, new[] { first, second }),
                Is.EqualTo(DismemberedPhysicsMode.None));
        }

        [Test]
        public void PhysicsDefinitionsAboveTheCutAreExcludedFromTheDetachedRig()
        {
            GameObject skeleton = Own(new GameObject("Skeleton"));
            Transform shoulder = CreateChild(skeleton.transform, "Shoulder");
            Transform arm = CreateChild(shoulder, "Arm");
            CreateChild(arm, "ForeArm");
            UMAPhysicsElement shoulderDefinition = Own(CreatePhysicsElement("Shoulder",
                "Shoulder", string.Empty, 1f));
            UMAPhysicsElement armDefinition = Own(CreatePhysicsElement("Arm", "Arm",
                "Shoulder", 1f));
            UMAPhysicsElement forearmDefinition = Own(CreatePhysicsElement("ForeArm",
                "ForeArm", "Arm", 1f));

            IReadOnlyList<UMAPhysicsElement> filtered =
                DismemberedRagdollBuilder.FilterDefinitionsForCutSubtree(arm,
                    new[] { shoulderDefinition, armDefinition, forearmDefinition });

            Assert.That(filtered, Has.Count.EqualTo(2));
            Assert.That(filtered[0], Is.SameAs(armDefinition));
            Assert.That(filtered[1], Is.SameAs(forearmDefinition));
        }

        [Test]
        public void RigidDetachedPhysicsCreatesACompoundColliderWithoutBoneBodies()
        {
            GameObject rig = Own(new GameObject("Rigid Detached Rig"));
            Transform upper = CreateChild(rig.transform, "Upper");
            Transform lower = CreateChild(upper, "Lower");
            UMAPhysicsElement upperDefinition = Own(CreatePhysicsElement("Upper Definition",
                "Upper", "Body Bone Not In This Cut", 2f, new ColliderDefinition
                {
                    colliderType = ColliderDefinition.ColliderType.Sphere,
                    sphereRadius = 0.25f
                }));
            UMAPhysicsElement lowerDefinition = Own(CreatePhysicsElement("Lower Definition",
                "Lower", "Upper", 1f, new ColliderDefinition
                {
                    colliderType = ColliderDefinition.ColliderType.Box,
                    boxDimensions = new Vector3(0.2f, 0.5f, 0.2f)
                }));

            bool built = DismemberedRagdollBuilder.TryBuildRigid(rig.transform,
                new[] { upperDefinition, upperDefinition, lowerDefinition }, 8,
                out DismemberedRagdollBuildResult result, out string error);

            Assert.That(built, Is.True, error);
            Assert.That(result.rigidbodies, Has.Length.EqualTo(1));
            Assert.That(result.rigidbodies[0], Is.SameAs(rig.GetComponent<Rigidbody>()));
            Assert.That(result.rigidbodies[0].mass, Is.EqualTo(3f));
            Assert.That(result.rootRigidbodies, Has.Length.EqualTo(1));
            Assert.That(result.colliders, Has.Length.EqualTo(2));
            Assert.That(result.joints, Is.Empty);
            Assert.That(upper.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(lower.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(upper.GetComponent<SphereCollider>().attachedRigidbody,
                Is.SameAs(result.rigidbodies[0]));
            Assert.That(lower.GetComponent<BoxCollider>().attachedRigidbody,
                Is.SameAs(result.rigidbodies[0]));
            Assert.That(rig.layer, Is.EqualTo(8));
            Assert.That(upper.gameObject.layer, Is.EqualTo(8));
            Assert.That(lower.gameObject.layer, Is.EqualTo(8));
        }

        [Test]
        public void DetachedRagdollRejectsMissingDefinitionBoneWithoutPartialPhysics()
        {
            GameObject rig = Own(new GameObject("Incomplete Detached Rig"));
            UMAPhysicsElement missing = Own(CreatePhysicsElement("Missing Definition",
                "Missing Bone", string.Empty, 1f, new ColliderDefinition
                {
                    colliderType = ColliderDefinition.ColliderType.Sphere,
                    sphereRadius = 0.1f
                }));

            bool built = DismemberedRagdollBuilder.TryBuild(rig.transform,
                new[] { missing }, 8, out DismemberedRagdollBuildResult result,
                out string error);

            Assert.That(built, Is.False);
            Assert.That(result, Is.Null);
            Assert.That(error, Does.Contain("Missing Bone"));
            Assert.That(rig.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(rig.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(rig.GetComponentsInChildren<CharacterJoint>(true), Is.Empty);
        }

        [Test]
        public void DetachedRagdollRollsBackComponentsAndLayersWhenConstructionFails()
        {
            GameObject rig = Own(new GameObject("Detached Rig"));
            Transform first = CreateChild(rig.transform, "First");
            Transform occupied = CreateChild(rig.transform, "Occupied");
            first.gameObject.layer = 3;
            occupied.gameObject.layer = 4;
            Rigidbody existingBody = occupied.gameObject.AddComponent<Rigidbody>();
            UMAPhysicsElement firstDefinition = Own(CreatePhysicsElement("First Definition",
                "First", string.Empty, 1f, new ColliderDefinition
                {
                    colliderType = ColliderDefinition.ColliderType.Box,
                    boxDimensions = Vector3.one
                }));
            UMAPhysicsElement occupiedDefinition = Own(CreatePhysicsElement(
                "Occupied Definition", "Occupied", string.Empty, 1f));

            bool built = DismemberedRagdollBuilder.TryBuild(rig.transform,
                new[] { firstDefinition, occupiedDefinition }, 8,
                out DismemberedRagdollBuildResult result, out string error);

            Assert.That(built, Is.False);
            Assert.That(result, Is.Null);
            StringAssert.Contains("already has a Rigidbody", error);
            Assert.That(first.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(first.GetComponent<Collider>(), Is.Null);
            Assert.That(first.gameObject.layer, Is.EqualTo(3));
            Assert.That(occupied.GetComponent<Rigidbody>(), Is.SameAs(existingBody));
            Assert.That(occupied.gameObject.layer, Is.EqualTo(4));
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
        public void ClothingBoundaryUsesTwoSidedBandWithoutAMeatCap()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(1, 0f));
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true,
                0.25f, 0.0001f, DismembermentCapUvMode.MeterScaledTiled,
                UmaDismemberment.DefaultCenteredCapUvPadding, -1,
                new[] { false }, new[] { true }, 10f, 0.5f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.EqualTo(1));
            Assert.That(result.capTriangleCount, Is.Zero);
            Assert.That(result.capSubmeshIndex, Is.EqualTo(-1));
            Assert.That(result.detachedMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(result.outerMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(result.detachedMesh.GetTriangles(0), Has.Length.EqualTo(18),
                "The three detached garment triangles should each have an interior face.");
            Assert.That(result.outerMesh.GetTriangles(0), Has.Length.EqualTo(6),
                "The retained garment triangle should have an interior face.");
            Assert.That(result.detachedMesh.vertexCount, Is.EqualTo(source.vertexCount));
            Assert.That(result.outerMesh.vertexCount, Is.EqualTo(source.vertexCount));
            AssertModernWeightsAreConsistent(result.detachedMesh);
            AssertModernWeightsAreConsistent(result.outerMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void SharedMaterialStillCapsBodyLoopButNotClothingLoop()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(2, 0f));
            var bodyVertices = new bool[source.vertexCount];
            var clothingVertices = new bool[source.vertexCount];
            for (int vertex = 0; vertex < source.vertexCount; vertex++)
            {
                bodyVertices[vertex] = vertex < 7;
                clothingVertices[vertex] = vertex >= 7;
            }
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true,
                0.25f, 0.0001f, DismembermentCapUvMode.MeterScaledTiled,
                UmaDismemberment.DefaultCenteredCapUvPadding, -1,
                new[] { true }, new[] { false }, 10f, 0.5f,
                bodyVertices, clothingVertices);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.EqualTo(2));
            Assert.That(result.capTriangleCount, Is.EqualTo(1),
                "Only the body boundary should receive an anatomical cap.");
            Assert.That(result.capSubmeshIndex, Is.EqualTo(1));
            Assert.That(result.detachedMesh.GetTriangles(0), Has.Length.EqualTo(27),
                "Only the garment shell should receive reversed interior triangles.");
            Assert.That(result.outerMesh.GetTriangles(0), Has.Length.EqualTo(9));
            Assert.That(result.detachedMesh.GetTriangles(1), Has.Length.EqualTo(3));
            Assert.That(result.outerMesh.GetTriangles(1), Has.Length.EqualTo(3));
            AssertModernWeightsAreConsistent(result.detachedMesh);
            AssertModernWeightsAreConsistent(result.outerMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void StrictCapsIgnoreOpenClothingBoundaryButStillValidateBody()
        {
            Mesh source = Own(CreateDuplicatedSeamShells(2, 0f));
            Vector3[] vertices = source.vertices;
            vertices[11] += Vector3.right * 0.001f;
            source.vertices = vertices;
            source.RecalculateBounds();
            var bodyVertices = new bool[source.vertexCount];
            var clothingVertices = new bool[source.vertexCount];
            for (int vertex = 0; vertex < source.vertexCount; vertex++)
            {
                bodyVertices[vertex] = vertex < 7;
                clothingVertices[vertex] = vertex >= 7;
            }
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, true,
                0.25f, 0.0001f, DismembermentCapUvMode.MeterScaledTiled,
                UmaDismemberment.DefaultCenteredCapUvPadding, -1,
                new[] { true }, new[] { false }, 10f, 0.5f,
                bodyVertices, clothingVertices);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.Success), error);
            Assert.That(result.boundaryLoopCount, Is.EqualTo(1),
                "The valid body loop should remain available while the open garment is tolerated.");
            Assert.That(result.capTriangleCount, Is.EqualTo(1));
            Assert.That(result.detachedMesh.GetTriangles(0), Has.Length.EqualTo(27));
            Assert.That(result.outerMesh.GetTriangles(0), Has.Length.EqualTo(9));
            AssertModernWeightsAreConsistent(result.detachedMesh);
            AssertModernWeightsAreConsistent(result.outerMesh);
            result.DestroyMeshes();
        }

        [Test]
        public void ClothingMajorityClassificationRejectsSingleMisweightedTriangleSpike()
        {
            Mesh source = Own(CreateOpenQuad());
            int[] originalTriangles = source.triangles;
            var options = new DismembermentMeshBuildOptions(0.5f, -1, true, false,
                0.25f, 0.0001f, DismembermentCapUvMode.MeterScaledTiled,
                UmaDismemberment.DefaultCenteredCapUvPadding, -1,
                new[] { false }, new[] { true }, 0.1f, 0.5f);

            DismembermentMeshBuildStatus status = DismembermentMeshBuilder.Build(source,
                new[] { false, true }, options, out DismembermentMeshBuildResult result,
                out string error);

            Assert.That(status, Is.EqualTo(DismembermentMeshBuildStatus.NoAffectedTriangles),
                error);
            Assert.That(result, Is.Null);
            Assert.That(source.triangles, Is.EqualTo(originalTriangles));
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
            component.capOnlyBodyParts = false;
            component.enabled = false;
            component.enabled = true;
            DismembermentResult completion = null;
            component.DismembermentCompleted.AddListener(result => completion = result);

            bool sliced = component.TrySlice(bones[4], 0.15f,
                out UmaDismemberment.DismemberedInfo info, out string failure);

            Assert.That(sliced, Is.True, failure);
            Assert.That(info.sourceRenderers, Has.Length.EqualTo(2));
            Assert.That(info.detachedRenderers, Has.Length.EqualTo(2));
            Assert.That(info.sourceTargetBone, Is.SameAs(bones[4]));
            Assert.That(info.cutSurfaces, Has.Length.EqualTo(2));
            Assert.That(info.cutSurfaces[0].IsValid, Is.True);
            Assert.That(completion, Is.Not.Null);
            Assert.That(completion.sourceTargetBone, Is.SameAs(bones[4]));
            Assert.That(completion.cutSurfaces, Has.Length.EqualTo(2));
            Assert.That(completion.cutSurfaces[0].sourceRenderer,
                Is.SameAs(info.sourceRenderers[0]));
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
        public void ComponentUndoRestoresMeshesDestroysLimbsAndAllowsTheCutAgain()
        {
            GameObject avatarObject = Own(new GameObject("Undo Dismemberment Test Avatar"));
            DynamicCharacterAvatar avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
            Transform root = CreateChild(avatarObject.transform, "Root");
            Transform global = CreateChild(root, "Global");
            Transform[] bones = CreateFiveBones(global);
            avatar.umaRoot = root.gameObject;
            avatar.skeleton = new UMASkeleton(global);
            Mesh original = Own(CreateWeightedTetrahedron(true));
            SkinnedMeshRenderer renderer = CreateRenderer(avatarObject.transform, "Body", global,
                bones, original);
            avatar.SetRenderers(new[] { renderer });
            UmaDismemberment component = avatarObject.AddComponent<UmaDismemberment>();
            component.generateCaps = false;
            component.capOnlyBodyParts = false;
            component.enabled = false;
            component.enabled = true;

            bool sliced = component.TrySlice(bones[4], 0.15f,
                out UmaDismemberment.DismemberedInfo firstCut, out string sliceFailure);
            Assert.That(sliced, Is.True, sliceFailure);
            Assert.That(renderer.sharedMesh, Is.Not.SameAs(original));
            Assert.That(firstCut.root, Is.Not.Null);

            bool undone = component.TryUndoDismemberment(out string undoFailure, false);

            Assert.That(undone, Is.True, undoFailure);
            Assert.That(renderer.sharedMesh, Is.SameAs(original));
            Assert.That(firstCut.root == null, Is.True);
            Assert.That(component.hasSplit, Is.Empty);

            bool slicedAgain = component.TrySlice(bones[4], 0.15f,
                out UmaDismemberment.DismemberedInfo secondCut, out string secondFailure);
            Assert.That(slicedAgain, Is.True, secondFailure);
            Assert.That(secondCut.root, Is.Not.Null);
        }

        [Test]
        public void ComponentSupportsNeckThenRightLegCutsOnTheSameRenderer()
        {
            Shader capShader = Shader.Find("UMA/Dismemberment/Cap Unlit");
            Assert.That(capShader, Is.Not.Null, "The sample cap shader must be importable.");
            Material capMaterial = Own(new Material(capShader));
            GameObject avatarObject = Own(new GameObject("Two Cut Dismemberment Test Avatar"));
            DynamicCharacterAvatar avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
            Transform root = CreateChild(avatarObject.transform, "Root");
            Transform global = CreateChild(root, "Global");
            Transform hips = CreateChild(global, "Hips");
            Transform neck = CreateChild(global, "Neck");
            Transform rightLeg = CreateChild(global, "RightUpperLeg");
            Transform[] bones = { hips, neck, rightLeg };
            avatar.umaRoot = root.gameObject;
            avatar.skeleton = new UMASkeleton(global);
            Mesh original = Own(CreateNeckAndRightLegCutMesh());
            SkinnedMeshRenderer renderer = CreateRenderer(avatarObject.transform, "UMARenderer",
                global, bones, original);
            renderer.sharedMaterials = new[] { capMaterial, capMaterial };
            avatar.SetRenderers(new[] { renderer });

            UmaDismemberment component = avatarObject.AddComponent<UmaDismemberment>();
            component.sliceFill = capMaterial;
            component.generateCaps = true;
            component.requireClosedCaps = true;
            component.capOnlyBodyParts = false;
            component.includeChildBones = false;
            component.enabled = false;
            component.enabled = true;

            bool neckSliced = component.TrySlice(neck, 0.5f,
                out UmaDismemberment.DismemberedInfo neckCut, out string neckFailure);

            Assert.That(neckSliced, Is.True, neckFailure);
            Assert.That(neckCut.detachedRenderers, Has.Length.EqualTo(1));
            Assert.That(neckCut.sourceRenderers, Has.Length.EqualTo(1));
            Assert.That(neckCut.cutSurfaces, Has.Length.GreaterThanOrEqualTo(1));
            AssertCutSurfacesUseRealSubmeshes(neckCut.cutSurfaces);
            Assert.That(renderer.sharedMesh.name,
                Is.EqualTo("Neck And Right Leg Test Mesh Dismembered Source"));
            AssertRendererMeshIsStructurallyValid(renderer);
            Mesh afterNeck = renderer.sharedMesh;
            AssertSameVertexLayout(original, afterNeck);
            VertexAttributeDescriptor[] afterNeckLayout = afterNeck.GetVertexAttributes();
            int[] afterNeckStrides = CaptureVertexBufferStrides(afterNeck,
                afterNeckLayout);
            Own(neckCut.root.gameObject);

            bool legSliced = component.TrySlice(rightLeg, 0.5f,
                out UmaDismemberment.DismemberedInfo legCut, out string legFailure);

            Assert.That(legSliced, Is.True, legFailure);
            Assert.That(legCut.detachedRenderers, Has.Length.EqualTo(1));
            Assert.That(legCut.sourceRenderers, Has.Length.EqualTo(1));
            Assert.That(legCut.cutSurfaces, Has.Length.GreaterThanOrEqualTo(1));
            AssertCutSurfacesUseRealSubmeshes(legCut.cutSurfaces);
            Assert.That(renderer.sharedMesh, Is.Not.SameAs(afterNeck));
            Assert.That(renderer.sharedMesh.name,
                Is.EqualTo("Neck And Right Leg Test Mesh Dismembered Source"),
                "Repeated cuts must not recursively rename the live source mesh.");
            Assert.That(neckCut.root, Is.Not.Null,
                "The neck piece must remain alive after the right-leg cut.");
            Assert.That(legCut.root, Is.Not.Null);
            AssertRendererMeshIsStructurallyValid(renderer);
            AssertSameVertexLayout(afterNeckLayout, afterNeckStrides,
                renderer.sharedMesh);
            Own(legCut.root.gameObject);
        }

        [Test]
        public void SourceRagdollCollidersAreSuspendedForTheCutSubtreeAndRestored()
        {
            GameObject avatarObject = Own(new GameObject("Source Collider Transfer Avatar"));
            avatarObject.AddComponent<DynamicCharacterAvatar>();
            UMAPhysicsAvatar physicsAvatar = avatarObject.AddComponent<UMAPhysicsAvatar>();
            UmaDismemberment dismemberment = avatarObject.AddComponent<UmaDismemberment>();
            Transform skeleton = CreateChild(avatarObject.transform, "Global");
            Transform cutBone = CreateChild(skeleton, "LeftArm");
            Transform cutChild = CreateChild(cutBone, "LeftForeArm");
            Transform retainedBone = CreateChild(skeleton, "Spine");

            BoxCollider cutCollider = cutBone.gameObject.AddComponent<BoxCollider>();
            SphereCollider childCollider = cutChild.gameObject.AddComponent<SphereCollider>();
            CapsuleCollider retainedCollider = retainedBone.gameObject
                .AddComponent<CapsuleCollider>();
            BoxCollider gameplayCollider = cutChild.gameObject.AddComponent<BoxCollider>();
            physicsAvatar.BoxColliders.Add(cutCollider);
            physicsAvatar.SphereColliders.Add(new ClothSphereColliderPair(childCollider));
            physicsAvatar.CapsuleColliders.Add(retainedCollider);

            int suspended = dismemberment.SuspendSourceRagdollColliders(cutBone);
            int suspendedAgain = dismemberment.SuspendSourceRagdollColliders(cutBone);

            Assert.That(suspended, Is.EqualTo(2));
            Assert.That(suspendedAgain, Is.Zero,
                "A collider must be tracked only once across overlapping requests.");
            Assert.That(cutCollider.enabled, Is.False);
            Assert.That(childCollider.enabled, Is.False);
            Assert.That(retainedCollider.enabled, Is.True);
            Assert.That(gameplayCollider.enabled, Is.True,
                "Colliders not owned by UMAPhysicsAvatar must remain unchanged.");

            dismemberment.ResetDismemberment(false);

            Assert.That(cutCollider.enabled, Is.True);
            Assert.That(childCollider.enabled, Is.True);
            Assert.That(retainedCollider.enabled, Is.True);
            Assert.That(gameplayCollider.enabled, Is.True);
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
            component.capOnlyBodyParts = false;
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

        private struct TestNormalTangent
        {
            public Vector3 normal;
            public Vector4 tangent;
        }

        private struct TestColorUv
        {
            public Color32 color;
            public Vector2 uv0;
            public Vector2 uv1;
        }

        private static Mesh CreateUmaMultistreamTetrahedron()
        {
            var mesh = new Mesh { name = "UMA Multistream Tetrahedron" };
            mesh.SetVertexBufferParams(4,
                new VertexAttributeDescriptor(VertexAttribute.Position,
                    VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal,
                    VertexAttributeFormat.Float32, 3, 1),
                new VertexAttributeDescriptor(VertexAttribute.Tangent,
                    VertexAttributeFormat.Float32, 4, 1),
                new VertexAttributeDescriptor(VertexAttribute.Color,
                    VertexAttributeFormat.UNorm8, 4, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0,
                    VertexAttributeFormat.Float32, 2, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1,
                    VertexAttributeFormat.Float32, 2, 2));
            var positions = new NativeArray<Vector3>(new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(-1f, 0f, -1f),
                new Vector3(1f, 0f, -1f),
                new Vector3(0f, 0f, 1f)
            }, Allocator.Temp);
            var normalTangents = new NativeArray<TestNormalTangent>(4, Allocator.Temp);
            var colorUv = new NativeArray<TestColorUv>(4, Allocator.Temp);
            try
            {
                for (int vertex = 0; vertex < 4; vertex++)
                {
                    normalTangents[vertex] = new TestNormalTangent
                    {
                        normal = vertex == 0 ? Vector3.up : Vector3.down,
                        tangent = new Vector4(1f, 0f, 0f, 1f)
                    };
                    colorUv[vertex] = new TestColorUv
                    {
                        color = new Color32((byte)(50 + vertex), (byte)(100 + vertex),
                            (byte)(150 + vertex), 255),
                        uv0 = vertex == 0 ? new Vector2(0.5f, 1f) :
                            vertex == 1 ? Vector2.zero :
                            vertex == 2 ? Vector2.right : Vector2.one,
                        uv1 = new Vector2(vertex * 0.1f, vertex * 0.2f)
                    };
                }
                mesh.SetVertexBufferData(positions, 0, 0, positions.Length, 0);
                mesh.SetVertexBufferData(normalTangents, 0, 0, normalTangents.Length, 1);
                mesh.SetVertexBufferData(colorUv, 0, 0, colorUv.Length, 2);
            }
            finally
            {
                positions.Dispose();
                normalTangents.Dispose();
                colorUv.Dispose();
            }
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 3, 1, 2, 3 };
            mesh.bindposes = CreateIdentityBindposes(2);
            SetWeights(mesh, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int GetStreamCount(VertexAttributeDescriptor[] layout)
        {
            int count = 0;
            for (int i = 0; i < layout.Length; i++) count = Mathf.Max(count, layout[i].stream + 1);
            return count;
        }

        private static void AssertPreservedVertexLayout(Mesh source, Mesh output,
            VertexAttributeDescriptor[] expected)
        {
            AssertSameVertexLayout(expected, CaptureVertexBufferStrides(source, expected),
                output);
        }

        private static void AssertSameVertexLayout(VertexAttributeDescriptor[] expected,
            int[] expectedStrides, Mesh actualMesh)
        {
            VertexAttributeDescriptor[] actual = actualMesh.GetVertexAttributes();
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i].attribute, Is.EqualTo(expected[i].attribute));
                Assert.That(actual[i].format, Is.EqualTo(expected[i].format));
                Assert.That(actual[i].dimension, Is.EqualTo(expected[i].dimension));
                Assert.That(actual[i].stream, Is.EqualTo(expected[i].stream));
            }
            Assert.That(expectedStrides, Has.Length.EqualTo(GetStreamCount(expected)));
            for (int stream = 0; stream < expectedStrides.Length; stream++)
                Assert.That(actualMesh.GetVertexBufferStride(stream),
                    Is.EqualTo(expectedStrides[stream]));
        }

        private static void AssertSameVertexLayout(Mesh expected, Mesh actual)
        {
            AssertPreservedVertexLayout(expected, actual, expected.GetVertexAttributes());
        }

        private static int[] CaptureVertexBufferStrides(Mesh mesh,
            VertexAttributeDescriptor[] layout)
        {
            var strides = new int[GetStreamCount(layout)];
            for (int stream = 0; stream < strides.Length; stream++)
                strides[stream] = mesh.GetVertexBufferStride(stream);
            return strides;
        }

        private static void AssertPreservedSourceStreamBytes(Mesh output,
            List<byte[]> sourceStreams, int sourceVertexCount,
            VertexAttributeDescriptor[] layout)
        {
            using Mesh.MeshDataArray outputData = Mesh.AcquireReadOnlyMeshData(output);
            for (int stream = 0; stream < sourceStreams.Count; stream++)
            {
                bool skinningStream = false;
                for (int attribute = 0; attribute < layout.Length; attribute++)
                    if (layout[attribute].stream == stream &&
                        (layout[attribute].attribute == VertexAttribute.BlendWeight ||
                         layout[attribute].attribute == VertexAttribute.BlendIndices))
                    {
                        skinningStream = true;
                        break;
                    }
                if (skinningStream) continue;
                NativeArray<byte> actual = outputData[0].GetVertexData<byte>(stream);
                int byteCount = sourceVertexCount * output.GetVertexBufferStride(stream);
                Assert.That(actual.Length, Is.GreaterThanOrEqualTo(byteCount));
                for (int i = 0; i < byteCount; i++)
                    Assert.That(actual[i], Is.EqualTo(sourceStreams[stream][i]),
                        $"Stream {stream} differs at byte {i}.");
            }
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
            var uv = new Vector2[vertices.Length];
            for (int shell = 0; shell < shellCount; shell++)
            {
                int vertex = shell * verticesPerShell;
                uv[vertex] = new Vector2(0.5f, 0.9f);
                uv[vertex + 1] = new Vector2(0.2f, 0.2f);
                uv[vertex + 2] = new Vector2(0.8f, 0.2f);
                uv[vertex + 3] = new Vector2(0.5f, 0.7f);
                uv[vertex + 4] = uv[vertex + 1];
                uv[vertex + 5] = uv[vertex + 2];
                uv[vertex + 6] = uv[vertex + 3];
            }
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.bindposes = CreateIdentityBindposes(2);
            mesh.SetBoneWeights(counts, weights);
            counts.Dispose();
            weights.Dispose();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateNeckAndRightLegCutMesh()
        {
            const int shellCount = 2;
            const int verticesPerShell = 8;
            var vertices = new Vector3[shellCount * verticesPerShell];
            var uv = new Vector2[vertices.Length];
            var primaryTriangles = new List<int>(shellCount * 15);
            var secondaryTriangles = new List<int>(shellCount * 3);
            var counts = new NativeArray<byte>(new byte[vertices.Length], Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(new BoneWeight1[vertices.Length],
                Allocator.Temp);
            try
            {
                for (int shell = 0; shell < shellCount; shell++)
                {
                    int vertex = shell * verticesPerShell;
                    Vector3 offset = shell == 0
                        ? new Vector3(0f, 1.5f, 0f)
                        : new Vector3(1.5f, -1f, 0f);
                    vertices[vertex] = offset + new Vector3(0f, 0.75f, 0f);
                    vertices[vertex + 1] = offset + new Vector3(-0.5f, 0f, -0.5f);
                    vertices[vertex + 2] = offset + new Vector3(0.5f, 0f, -0.5f);
                    vertices[vertex + 3] = offset + new Vector3(0f, 0f, 0.5f);
                    vertices[vertex + 4] = vertices[vertex + 1];
                    vertices[vertex + 5] = vertices[vertex + 2];
                    vertices[vertex + 6] = vertices[vertex + 3];
                    vertices[vertex + 7] =
                        (vertices[vertex + 4] + vertices[vertex + 5] +
                         vertices[vertex + 6]) / 3f;
                    uv[vertex] = new Vector2(0.5f, 0.9f);
                    uv[vertex + 1] = new Vector2(0.2f, 0.2f);
                    uv[vertex + 2] = new Vector2(0.8f, 0.2f);
                    uv[vertex + 3] = new Vector2(0.5f, 0.7f);
                    uv[vertex + 4] = uv[vertex + 1];
                    uv[vertex + 5] = uv[vertex + 2];
                    uv[vertex + 6] = uv[vertex + 3];
                    uv[vertex + 7] =
                        (uv[vertex + 4] + uv[vertex + 5] + uv[vertex + 6]) / 3f;
                    primaryTriangles.Add(vertex);
                    primaryTriangles.Add(vertex + 2);
                    primaryTriangles.Add(vertex + 1);
                    primaryTriangles.Add(vertex);
                    primaryTriangles.Add(vertex + 3);
                    primaryTriangles.Add(vertex + 2);
                    primaryTriangles.Add(vertex);
                    primaryTriangles.Add(vertex + 1);
                    primaryTriangles.Add(vertex + 3);
                    // Split the remaining side of the cut across two material submeshes. This
                    // reproduces the mixed neck boundary that used to escape as submesh -1.
                    primaryTriangles.Add(vertex + 7);
                    primaryTriangles.Add(vertex + 4);
                    primaryTriangles.Add(vertex + 5);
                    primaryTriangles.Add(vertex + 7);
                    primaryTriangles.Add(vertex + 6);
                    primaryTriangles.Add(vertex + 4);
                    secondaryTriangles.Add(vertex + 7);
                    secondaryTriangles.Add(vertex + 5);
                    secondaryTriangles.Add(vertex + 6);
                    for (int localVertex = 0; localVertex < verticesPerShell; localVertex++)
                    {
                        counts[vertex + localVertex] = 1;
                        weights[vertex + localVertex] = new BoneWeight1
                        {
                            boneIndex = localVertex <= 3 ? shell + 1 : 0,
                            weight = 1f
                        };
                    }
                }

                var mesh = new Mesh { name = "Neck And Right Leg Test Mesh" };
                mesh.vertices = vertices;
                mesh.uv = uv;
                mesh.subMeshCount = 2;
                mesh.SetTriangles(primaryTriangles, 0);
                mesh.SetTriangles(secondaryTriangles, 1);
                mesh.bindposes = CreateIdentityBindposes(3);
                mesh.SetBoneWeights(counts, weights);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
            finally
            {
                counts.Dispose();
                weights.Dispose();
            }
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
            int total = 0;
            for (int i = 0; i < counts.Length; i++) total += counts[i];
            Assert.That(total, Is.EqualTo(weights.Length));
        }

        private static void AssertCutSurfacesUseRealSubmeshes(
            DismembermentCutSurface[] surfaces)
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                Assert.That(surfaces[i].sourceSubmeshIndex, Is.GreaterThanOrEqualTo(0),
                    "The internal mixed-submesh sentinel must not escape into runtime decals.");
                Assert.That(surfaces[i].IsValid, Is.True);
            }
        }

        private void AssertRendererMeshIsStructurallyValid(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            AssertModernWeightsAreConsistent(mesh);
            VertexAttributeDescriptor[] layout = mesh.GetVertexAttributes();
            using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                int streamCount = GetStreamCount(layout);
                for (int stream = 0; stream < streamCount; stream++)
                {
                    int stride = mesh.GetVertexBufferStride(stream);
                    Assert.That(stride, Is.GreaterThan(0));
                    Assert.That(data[0].GetVertexData<byte>(stream).Length,
                        Is.EqualTo(mesh.vertexCount * stride),
                        $"Vertex stream {stream} must match vertexCount * stride.");
                }
            }

            Mesh baked = Own(new Mesh { name = mesh.name + " Test Bake" });
            renderer.BakeMesh(baked);
            Assert.That(baked.vertexCount, Is.EqualTo(mesh.vertexCount));
            Assert.That(baked.vertices, Has.Length.EqualTo(mesh.vertexCount));
        }

        private static void AssertVertexUsesBone(Mesh mesh, int vertexIndex, int expectedBone)
        {
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            int offset = 0;
            for (int vertex = 0; vertex < vertexIndex; vertex++) offset += counts[vertex];
            Assert.That(counts[vertexIndex], Is.GreaterThan(0));
            bool found = false;
            for (int influence = 0; influence < counts[vertexIndex]; influence++)
                if (weights[offset + influence].boneIndex == expectedBone) found = true;
            Assert.That(found, Is.True,
                $"Vertex {vertexIndex} should retain a weight from bone {expectedBone}.");
        }

        private static void AssertAllWeightsUseBone(Mesh mesh, int expectedBone)
        {
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            int offset = 0;
            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                Assert.That(counts[vertex], Is.GreaterThan(0));
                float total = 0f;
                for (int influence = 0; influence < counts[vertex]; influence++)
                {
                    BoneWeight1 weight = weights[offset++];
                    Assert.That(weight.boneIndex, Is.EqualTo(expectedBone),
                        $"Vertex {vertex} retained a cross-cut bone influence.");
                    total += weight.weight;
                }
                Assert.That(total, Is.EqualTo(1f).Within(0.000001f));
            }
            Assert.That(offset, Is.EqualTo(weights.Length));
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

        private static UMAPhysicsElement CreatePhysicsElement(string name, string boneName,
            string parentBone, float mass, params ColliderDefinition[] colliders)
        {
            UMAPhysicsElement definition = ScriptableObject.CreateInstance<UMAPhysicsElement>();
            definition.name = name;
            definition.boneName = boneName;
            definition.parentBone = parentBone;
            definition.mass = mass;
            definition.colliders = colliders;
            return definition;
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
