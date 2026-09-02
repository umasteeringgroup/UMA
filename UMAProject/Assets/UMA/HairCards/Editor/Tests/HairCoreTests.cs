#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Utils;
using UMA.HairCards.Runtime;

namespace UMA.HairCards.Editor.Tests
{
    public sealed class HairCoreTests
    {
        private Mesh sourceMesh;
        private HairGroomAsset groom;
        private HairCardProfileAsset profile;

        [SetUp]
        public void SetUp()
        {
            sourceMesh = new Mesh { name = "Test Scalp" };
            sourceMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0f, 0f, 0.5f)
            };
            sourceMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            sourceMesh.triangles = new[] { 0, 1, 2 };

            profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.Configure(HairCardShape.Ribbon, 0.04f, 0f, 4, generateBackfaces: true);

            groom = ScriptableObject.CreateInstance<HairGroomAsset>();
            groom.SetSource(sourceMesh, "mesh:test", "TestRace", "TestScalp");
            groom.Lods[0].samplesPerCard = 4;
            HairGroup group = groom.Groups[0];
            group.profile = profile;
            group.children.childrenPerGuide = 0;
            group.guides.Add(CreateGuide("Guide A", 11, new Vector3(0f, 0f, 0f)));
            groom.EnsureIntegrity();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(groom);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(sourceMesh);
        }

        [Test]
        public void GroomIntegrityCreatesStableSerializableIdentitiesAndDefaultData()
        {
            Assert.That(groom.GroomId, Is.Not.Null.And.Not.Empty);
            Assert.That(groom.Groups, Has.Count.EqualTo(1));
            Assert.That(groom.Groups[0].Id, Is.Not.Null.And.Not.Empty);
            Assert.That(groom.Groups[0].guides[0].Id, Is.Not.Null.And.Not.Empty);
            Assert.That(groom.Groups[0].FindMap(HairMapKind.GrowthArea), Is.Not.Null);
            Assert.That(groom.Groups[0].FindMap(HairMapKind.GrowthArea).values,
                Has.Length.EqualTo(sourceMesh.vertexCount));
            Assert.That(groom.SourceTopologyMatches(), Is.True);
        }

        [Test]
        public void SurfaceAnchorEvaluatesBarycentricPositionAndNormal()
        {
            HairSurfaceAnchor anchor = HairSurfaceAnchor.Create("mesh:test", 0, 0,
                new Vector3(0.25f, 0.25f, 0.5f), 0.1f, Vector3.zero, Vector3.up);

            bool success = HairMeshUtility.TryEvaluateAnchor(sourceMesh, anchor,
                out Vector3 position, out Vector3 normal);

            Assert.That(success, Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(0f, 0.1f, 0f)).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(normal, Is.EqualTo(Vector3.up).Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void AuthoringPoseMapsGuidesFromSourceTriangleOntoBakedCharacterTriangle()
        {
            Mesh posedMesh = new Mesh { name = "Posed Test Scalp" };
            Matrix4x4 expectedTransform = Matrix4x4.TRS(new Vector3(2f, 3f, -1f),
                Quaternion.Euler(90f, 0f, 35f), Vector3.one);
            Vector3[] posedVertices = new Vector3[sourceMesh.vertexCount];
            for (int vertex = 0; vertex < posedVertices.Length; vertex++)
                posedVertices[vertex] = expectedTransform.MultiplyPoint3x4(sourceMesh.vertices[vertex]);
            posedMesh.vertices = posedVertices;
            posedMesh.triangles = sourceMesh.triangles;
            try
            {
                HairSurfaceAnchor anchor = HairSurfaceAnchor.Create("mesh:test", 0, 0,
                    new Vector3(0.2f, 0.3f, 0.5f), 0f, Vector3.zero, Vector3.up);
                HairAuthoringPose pose = new HairAuthoringPose(sourceMesh, posedMesh);

                bool found = pose.TryGetMatrix(anchor, out Matrix4x4 actualTransform);
                bool posedPointFound = pose.TryPoseTrianglePoint(0, anchor.Barycentric,
                    out Vector3 posedPoint, out _);
                Vector3 guidePoint = new Vector3(0.15f, 0.4f, -0.1f);
                Vector3 sourcePoint = sourceMesh.vertices[0] * anchor.Barycentric.x +
                                      sourceMesh.vertices[1] * anchor.Barycentric.y +
                                      sourceMesh.vertices[2] * anchor.Barycentric.z;

                Assert.That(found, Is.True);
                Assert.That(posedPointFound, Is.True);
                Assert.That(posedPoint, Is.EqualTo(expectedTransform.MultiplyPoint3x4(sourcePoint))
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(actualTransform.MultiplyPoint3x4(guidePoint),
                    Is.EqualTo(expectedTransform.MultiplyPoint3x4(guidePoint))
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
            }
            finally
            {
                Object.DestroyImmediate(posedMesh);
            }
        }

        [Test]
        public void ChildGenerationIsDeterministicAndReportsExpectedCardCount()
        {
            groom.Groups[0].children.childrenPerGuide = 3;

            HairEvaluationResult first = HairGroomEvaluator.Evaluate(groom);
            HairEvaluationResult second = HairGroomEvaluator.Evaluate(groom);

            Assert.That(first.CardCount, Is.EqualTo(4));
            Assert.That(first.guideCurveCount, Is.EqualTo(1));
            Assert.That(first.childCurveCount, Is.EqualTo(3));
            Assert.That(second.CardCount, Is.EqualTo(first.CardCount));
            for (int curveIndex = 0; curveIndex < first.curves.Count; curveIndex++)
            {
                Assert.That(second.curves[curveIndex].curveId, Is.EqualTo(first.curves[curveIndex].curveId));
                Assert.That(second.curves[curveIndex].points.Count, Is.EqualTo(first.curves[curveIndex].points.Count));
                for (int pointIndex = 0; pointIndex < first.curves[curveIndex].points.Count; pointIndex++)
                {
                    Assert.That(second.curves[curveIndex].points[pointIndex].position,
                        Is.EqualTo(first.curves[curveIndex].points[pointIndex].position)
                            .Using(Vector3ComparerWithEqualsOperator.Instance));
                }
            }
        }

        [Test]
        public void RibbonMesherBuildsExpectedTopologyAndValidAttributes()
        {
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Ribbon Test");

            Assert.That(build.cardCount, Is.EqualTo(1));
            Assert.That(build.vertexCount, Is.EqualTo(8));
            Assert.That(build.triangleCount, Is.EqualTo(10));
            Assert.That(build.mesh.vertexCount, Is.EqualTo(build.vertexCount));
            Assert.That(build.mesh.uv, Has.Length.EqualTo(build.vertexCount));
            Assert.That(build.mesh.normals, Has.Length.EqualTo(build.vertexCount));
            Assert.That(build.degenerateTriangleCount, Is.Zero);
        }

        [Test]
        public void AtlasSelectionUsesOnlyChosenNumberedAreasAndMapsCardUvsIntoTheirRectangles()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            try
            {
                HairAtlasRegion[] areas = new HairAtlasRegion[7];
                for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
                {
                    areas[areaIndex] = atlas.CreateRegion($"Area {areaIndex + 1}",
                        new Rect(areaIndex / 8f, 0f, 0.1f, 1f), areaIndex + 1f);
                }

                List<string> selected = new List<string>
                {
                    areas[1].Id,
                    areas[2].Id,
                    areas[6].Id
                };
                HashSet<string> observed = new HashSet<string>();
                for (int sample = 0; sample < 512; sample++)
                {
                    uint randomValue = (uint)((ulong)uint.MaxValue * (uint)sample / 511u);
                    HairAtlasRegion chosen = atlas.GetWeightedRegion(randomValue,
                        HairAtlasRegionSelectionMode.Selected, selected);
                    Assert.That(chosen, Is.Not.Null);
                    Assert.That(selected, Does.Contain(chosen.Id));
                    observed.Add(chosen.Id);
                }
                Assert.That(observed, Is.EquivalentTo(selected));
                Assert.That(atlas.GetWeightedRegion(123u, HairAtlasRegionSelectionMode.Selected,
                    new[] { "missing-area" }), Is.Null);

                HairGroup group = groom.Groups[0];
                group.atlas = atlas;
                group.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
                group.atlasRegionIds.Clear();
                group.atlasRegionIds.Add(areas[2].Id);
                group.children.childrenPerGuide = 2;
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
                using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Selected UV Area");
                Assert.That(build.cardCount, Is.EqualTo(3));
                foreach (Vector2 uv in build.mesh.uv)
                {
                    Assert.That(uv.x, Is.InRange(areas[2].uvRect.x, areas[2].uvRect.xMax));
                    Assert.That(uv.y, Is.InRange(areas[2].uvRect.y, areas[2].uvRect.yMax));
                }
            }
            finally
            {
                groom.Groups[0].atlas = null;
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void ValidatorBlocksSelectedAtlasModeWhenNoSelectedAreaExists()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            try
            {
                atlas.CreateRegion("Area 1", new Rect(0f, 0f, 1f, 1f));
                HairGroup group = groom.Groups[0];
                group.atlas = atlas;
                group.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
                group.atlasRegionIds.Clear();
                group.atlasRegionIds.Add("deleted-area");

                HairValidationReport report = HairValidator.Validate(groom);

                Assert.That(report.issues.Exists(issue => issue.code == HairValidationCode.MissingAtlasRegion),
                    Is.True);
            }
            finally
            {
                groom.Groups[0].atlas = null;
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void TubeMesherBuildsConfigurableSideCount()
        {
            profile.Configure(HairCardShape.TaperedTube, 0.04f, 0.002f, 4, sideCount: 5,
                generateBackfaces: false);
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Tube Test");

            Assert.That(build.vertexCount, Is.EqualTo(20));
            Assert.That(build.triangleCount, Is.EqualTo(30));
            Assert.That(build.degenerateTriangleCount, Is.Zero);
        }

        [Test]
        public void ValidatorReportsMissingAtlasButAllowsGeometryWhenAtlasIsOptional()
        {
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation);

            HairValidationReport report = HairValidator.Validate(groom, evaluation, build,
                new HairValidationOptions { requireAtlas = false });

            Assert.That(report.ErrorCount, Is.Zero);
            Assert.That(report.WarningCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(report.issues.Exists(issue => issue.code == HairValidationCode.MissingAtlas), Is.True);
        }

        [Test]
        public void SourceTopologyChangeIsDetectedWithoutUsingTransientObjectIdentity()
        {
            sourceMesh.triangles = new[] { 0, 2, 1 };

            Assert.That(groom.SourceTopologyMatches(), Is.False);
            HairValidationReport report = HairValidator.Validate(groom);
            Assert.That(report.issues.Exists(issue => issue.code == HairValidationCode.SourceTopologyChanged), Is.True);
        }

        [Test]
        public void SourceVisibilityRemovesHiddenSlotTrianglesAndPreservesTriangleOwnership()
        {
            Mesh mesh = new Mesh { name = "Visibility Source" };
            SlotDataAsset firstAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset secondAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                mesh.vertices = new[]
                {
                    Vector3.zero, Vector3.right, Vector3.up,
                    Vector3.forward, Vector3.forward + Vector3.right, Vector3.forward + Vector3.up
                };
                mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
                firstAsset.name = "Head";
                firstAsset.meshData = new UMAMeshData { vertexCount = 3 };
                secondAsset.name = "Helmet";
                secondAsset.meshData = new UMAMeshData { vertexCount = 3 };
                SlotData first = new SlotData(firstAsset) { vertexOffset = 0 };
                SlotData second = new SlotData(secondAsset) { vertexOffset = 3 };
                Dictionary<string, SlotData> slots = new Dictionary<string, SlotData>
                {
                    { first.slotName, first },
                    { second.slotName, second }
                };

                using HairSourceVisibility visibility = new HairSourceVisibility(mesh, null, null, slots);
                Mesh all = visibility.Rebuild(new HashSet<string>());
                Assert.That(all.triangles.Length / 3, Is.EqualTo(2));

                Mesh filtered = visibility.Rebuild(new HashSet<string> { first.slotName });
                Assert.That(filtered.triangles.Length / 3, Is.EqualTo(1));
                Assert.That(visibility.IsVertexVisible(0, new HashSet<string> { first.slotName }), Is.False);
                Assert.That(visibility.IsVertexVisible(3, new HashSet<string> { first.slotName }), Is.True);
                Assert.That(visibility.TryResolveVisibleTriangle(0, out HairSourceVisibility.TriangleReference triangle),
                    Is.True);
                Assert.That(triangle.Triangle, Is.EqualTo(1));
                Assert.That(new[] { triangle.A, triangle.B, triangle.C }, Is.EqualTo(new[] { 3, 4, 5 }));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SurfaceRaycasterHitsReadableMeshWithoutEditorPhysics()
        {
            HairMeshRaycaster raycaster = new HairMeshRaycaster(sourceMesh);

            bool found = raycaster.Raycast(new Ray(Vector3.up, Vector3.down),
                out HairMeshRaycastHit hit);

            Assert.That(found, Is.True);
            Assert.That(hit.TriangleIndex, Is.Zero);
            Assert.That(hit.Point, Is.EqualTo(Vector3.zero)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(hit.Normal, Is.EqualTo(Vector3.up)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(hit.Barycentric.x + hit.Barycentric.y + hit.Barycentric.z,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(raycaster.TryGetTriangleVertices(hit.TriangleIndex,
                out int a, out int b, out int c), Is.True);
            Assert.That(new[] { a, b, c }, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void SourceVisibilityUsesPosedSurfaceWhileKeepingSourceTriangleReferences()
        {
            Mesh posed = Object.Instantiate(sourceMesh);
            try
            {
                Vector3[] posedVertices = posed.vertices;
                for (int i = 0; i < posedVertices.Length; i++) posedVertices[i] += Vector3.up * 2f;
                posed.vertices = posedVertices;
                posed.RecalculateBounds();

                using HairSourceVisibility visibility = new HairSourceVisibility(sourceMesh, posed,
                    null, null, null);
                Mesh visible = visibility.Rebuild(new HashSet<string>());

                Assert.That(visible.vertices[0], Is.EqualTo(sourceMesh.vertices[0] + Vector3.up * 2f)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(visibility.TryResolveVisibleTriangle(0,
                    out HairSourceVisibility.TriangleReference triangle), Is.True);
                Assert.That(new[] { triangle.A, triangle.B, triangle.C }, Is.EqualTo(new[] { 0, 1, 2 }));
            }
            finally
            {
                Object.DestroyImmediate(posed);
            }
        }

        [Test]
        public void VisibilityCatalogGroupsUdimMembersWhileRetainingIndividualSlots()
        {
            SlotDataAsset firstAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset secondAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                firstAsset.name = "Face1001";
                firstAsset.udimGroupId = "face";
                firstAsset.udimGroupName = "Face UDIM";
                firstAsset.udimTileNumber = 1001;
                secondAsset.name = "Face1002";
                secondAsset.udimGroupId = "face";
                secondAsset.udimGroupName = "Face UDIM";
                secondAsset.udimTileNumber = 1002;
                Dictionary<string, SlotData> slots = new Dictionary<string, SlotData>
                {
                    { firstAsset.slotName, new SlotData(firstAsset) },
                    { secondAsset.slotName, new SlotData(secondAsset) }
                };

                HairAvatarVisibilityCatalog catalog = HairAvatarVisibilityCatalog.Build(null, slots);

                Assert.That(catalog.UdimGroups, Has.Count.EqualTo(1));
                Assert.That(catalog.UdimGroups[0].SlotNames,
                    Is.EquivalentTo(new[] { firstAsset.slotName, secondAsset.slotName }));
                Assert.That(catalog.SlotGroups, Has.Count.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
            }
        }

        [Test]
        public void GuideGeneratorUsesGrowthAreaAndIsDeterministic()
        {
            HairGroup group = groom.Groups[0];
            HairGrowthMap growth = group.FindMap(HairMapKind.GrowthArea);
            for (int i = 0; i < growth.values.Length; i++) growth.values[i] = 1f;
            HairGuideGenerationSettings settings = new HairGuideGenerationSettings
            {
                guideCount = 8,
                minimumRootSpacing = 0f,
                defaultLength = 0.2f,
                seed = 42
            };

            HairGuideGenerationResult first = HairGuideGenerator.Generate(groom, group, settings);
            HairGuideGenerationResult second = HairGuideGenerator.Generate(groom, group, settings);

            Assert.That(first.guides, Has.Count.EqualTo(8));
            Assert.That(second.guides, Has.Count.EqualTo(8));
            for (int i = 0; i < first.guides.Count; i++)
            {
                Assert.That(second.guides[i].root.CachedLocalPosition,
                    Is.EqualTo(first.guides[i].root.CachedLocalPosition)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(first.guides[i].root.IsValid, Is.True);
            }
        }

        [Test]
        public void UniformLowGrowthStrengthIsNotAppliedTwiceDuringGuideDistribution()
        {
            HairGroup group = groom.Groups[0];
            HairGrowthMap growth = group.FindMap(HairMapKind.GrowthArea);
            for (int vertex = 0; vertex < growth.values.Length; vertex++) growth.values[vertex] = 0.2f;
            HairGuideGenerationSettings settings = new HairGuideGenerationSettings
            {
                guideCount = 8,
                pointsPerGuide = 4,
                minimumRootSpacing = 0f,
                defaultLength = 0.2f,
                seed = 31415
            };

            HairGuideGenerationResult result = HairGuideGenerator.Generate(groom, group, settings);

            Assert.That(result.guides, Has.Count.EqualTo(8));
            Assert.That(result.rejectedByMask, Is.Zero,
                "Uniform strength should scale triangle density, not be reused as a second rejection probability.");
        }

        [TestCase(HairWorkflowStep.Setup, HairSceneTool.Select, HairPreviewMode.Cards)]
        [TestCase(HairWorkflowStep.Growth, HairSceneTool.PaintGrowth, HairPreviewMode.GrowthMap)]
        [TestCase(HairWorkflowStep.Guides, HairSceneTool.Select, HairPreviewMode.Guides)]
        [TestCase(HairWorkflowStep.Groom, HairSceneTool.Comb, HairPreviewMode.GuidesAndChildren)]
        [TestCase(HairWorkflowStep.Cards, HairSceneTool.Select, HairPreviewMode.Cards)]
        [TestCase(HairWorkflowStep.Optimize, HairSceneTool.Select, HairPreviewMode.Cards)]
        [TestCase(HairWorkflowStep.ValidateAndBake, HairSceneTool.Select, HairPreviewMode.Cards)]
        public void WorkflowStepsChooseScopedToolsAndUsefulPreviews(HairWorkflowStep step,
            HairSceneTool expectedTool, HairPreviewMode expectedPreview)
        {
            Assert.That(HairWorkflowState.DefaultTool(step), Is.EqualTo(expectedTool));
            Assert.That(HairWorkflowState.DefaultPreview(step), Is.EqualTo(expectedPreview));
            Assert.That(HairWorkflowState.IsToolAllowed(step, expectedTool), Is.True);
        }

        [Test]
        public void PaintToolCannotRemainActiveInGuidesGroomOrOutputSteps()
        {
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Growth,
                HairSceneTool.PaintGrowth), Is.True);
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Guides,
                HairSceneTool.PaintGrowth), Is.False);
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Groom,
                HairSceneTool.PaintGrowth), Is.False);
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Cards,
                HairSceneTool.PaintGrowth), Is.False);
            Assert.That(HairWorkflowState.StepForTool(HairSceneTool.PlaceGuide,
                HairWorkflowStep.Growth), Is.EqualTo(HairWorkflowStep.Guides));
            Assert.That(HairWorkflowState.StepForTool(HairSceneTool.Comb,
                HairWorkflowStep.Guides), Is.EqualTo(HairWorkflowStep.Groom));
        }

        [Test]
        public void GroomBrushPicksTheWholeGuideSegmentInsteadOfOnlyControlPoints()
        {
            Ray ray = new Ray(new Vector3(0f, 1f, -2f), Vector3.forward);

            bool found = HairCurveBrushUtility.TryClosestPoint(ray,
                new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
                out Vector3 point, out float squareDistance);

            Assert.That(found, Is.True);
            Assert.That(point, Is.EqualTo(Vector3.zero).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(squareDistance, Is.EqualTo(1f).Within(0.00001f));
        }

        [Test]
        public void BrushFalloffMatchesOverlayPainterHardnessModel()
        {
            const float radius = 2f;
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(0f, radius, 0.75f), Is.EqualTo(1f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1.5f, radius, 0.75f), Is.EqualTo(1f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1.75f, radius, 0.75f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(2f, radius, 0.75f), Is.Zero);
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1f, radius, 0f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1.99f, radius, 1f), Is.EqualTo(1f));
        }

        [Test]
        public void BrushResizeControlsMatchOverlayPainterSensitivityAndLimits()
        {
            float resized = HairBrushInteractionUtility.RadiusFromModifierDrag(0.05f, 100f);
            Assert.That(resized, Is.EqualTo(0.05f * Mathf.Exp(1.2f)).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.HardnessFromModifierDrag(0.75f, 90f),
                Is.EqualTo(0.25f).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.StepRadius(0.05f, 1f),
                Is.EqualTo(0.056f).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.StepHardness(0.75f, -1f),
                Is.EqualTo(0.70f).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.RadiusFromModifierDrag(0.5f, 1000f),
                Is.EqualTo(HairBrushInteractionUtility.MaximumRadius));
        }

        [Test]
        public void MirroredBrushUsesLocalXPlaneAndMaximumFalloff()
        {
            Vector3 center = new Vector3(0.5f, 0.1f, -0.2f);
            Vector3 mirroredCenter = HairBrushInteractionUtility.MirrorX(center);

            Assert.That(mirroredCenter, Is.EqualTo(new Vector3(-0.5f, 0.1f, -0.2f))
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(HairBrushInteractionUtility.EvaluateMirroredFalloff(
                mirroredCenter, center, 0.25f, 0.75f, true), Is.EqualTo(1f));
            Assert.That(HairBrushInteractionUtility.EvaluateMirroredFalloff(
                mirroredCenter, center, 0.25f, 0.75f, false), Is.Zero);

            // Taking the maximum prevents the two footprints from doubling strength on the centerline.
            float centerline = HairBrushInteractionUtility.EvaluateMirroredFalloff(
                Vector3.zero, Vector3.zero, 1f, 0.5f, true);
            Assert.That(centerline, Is.EqualTo(
                HairBrushInteractionUtility.EvaluateFalloff(0f, 1f, 0.5f)));
        }

        [Test]
        public void VerticalSlicePaintGenerateAcceptStyleChildrenMeshAndValidate()
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear();
            group.children.childrenPerGuide = 3;
            group.children.includeGuideCard = true;
            HairGrowthMap growth = group.FindMap(HairMapKind.GrowthArea);
            for (int vertex = 0; vertex < growth.values.Length; vertex++) growth.values[vertex] = 1f;
            HairGuideGenerationSettings settings = new HairGuideGenerationSettings
            {
                guideCount = 4,
                pointsPerGuide = 6,
                defaultLength = 0.2f,
                minimumRootSpacing = 0f,
                seed = 9081
            };

            HairGuideGenerationResult preview = HairGuideGenerator.Generate(groom, group, settings);
            Assert.That(preview.guides, Has.Count.EqualTo(4));

            int accepted = HairGroomCommands.AddGeneratedGuides(groom, group, preview.guides);
            Assert.That(accepted, Is.EqualTo(4));
            Assert.That(group.guides, Has.Count.EqualTo(4));

            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group, "Shape");
            HairGuide styledGuide = group.guides[0];
            HairGuideDelta delta = new HairGuideDelta
            {
                guideId = styledGuide.Id,
                positionOffsets = new Vector3[styledGuide.points.Count],
                widthOffsets = new float[styledGuide.points.Count],
                rollOffsets = new float[styledGuide.points.Count]
            };
            delta.positionOffsets[delta.positionOffsets.Length - 1] = Vector3.right * 0.05f;
            layer.deltas.Add(delta);

            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            Assert.That(evaluation.evaluatedGuides, Has.Count.EqualTo(4));
            Assert.That(evaluation.guideCurveCount, Is.EqualTo(4));
            Assert.That(evaluation.childCurveCount, Is.EqualTo(12));
            Assert.That(evaluation.CardCount, Is.EqualTo(16));
            Assert.That(evaluation.evaluatedGuides[0].points[evaluation.evaluatedGuides[0].points.Count - 1]
                .position.x, Is.GreaterThan(styledGuide.points[styledGuide.points.Count - 1].position.x));

            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Vertical Slice");
            Assert.That(build.cardCount, Is.EqualTo(16));
            Assert.That(build.vertexCount, Is.GreaterThan(0));
            Assert.That(build.triangleCount, Is.GreaterThan(0));
            Assert.That(build.degenerateTriangleCount, Is.Zero);

            HairValidationReport report = HairValidator.Validate(groom, evaluation, build,
                new HairValidationOptions { requireAtlas = false });
            Assert.That(report.ErrorCount, Is.Zero);
            Assert.That(report.cardCount, Is.EqualTo(16));

            HairBakeOutcome dryRun = HairBakePipeline.DryRun(groom);
            Assert.That(dryRun.succeeded, Is.True);
            Assert.That(dryRun.cardCount, Is.EqualTo(16));
            Assert.That(dryRun.triangleCount, Is.GreaterThan(0));
        }

        [Test]
        public void LockedGroupRejectsGeneratedGuideAcceptance()
        {
            HairGroup group = groom.Groups[0];
            group.locked = true;
            int originalCount = group.guides.Count;
            int accepted = HairGroomCommands.AddGeneratedGuides(groom, group,
                new[] { CreateGuide("Blocked", 42, Vector3.zero) });

            Assert.That(accepted, Is.Zero);
            Assert.That(group.guides, Has.Count.EqualTo(originalCount));
        }

        [Test]
        public void ChildDomainModifierChangesChildrenWithoutChangingGuideCard()
        {
            HairGroup group = groom.Groups[0];
            group.children.childrenPerGuide = 1;
            HairModifierSettings modifier = new HairModifierSettings
            {
                name = "Wide Children",
                type = HairModifierType.Width,
                domain = HairModifierDomain.Children,
                amount = 2f,
                weight = 1f
            };
            modifier.EnsureIntegrity();
            group.modifiers.Add(modifier);

            HairEvaluationResult result = HairGroomEvaluator.Evaluate(groom);

            Assert.That(result.CardCount, Is.EqualTo(2));
            HairEvaluatedCurve guide = result.curves.Find(curve => !curve.isChild);
            HairEvaluatedCurve child = result.curves.Find(curve => curve.isChild);
            Assert.That(guide, Is.Not.Null);
            Assert.That(child, Is.Not.Null);
            Assert.That(child.points[0].width, Is.GreaterThan(guide.points[0].width));
        }

        [Test]
        public void SkinningTransfersClosestSourceWeightsAndBindPoses()
        {
            sourceMesh.bindposes = new[] { Matrix4x4.identity };
            sourceMesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }
            };
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation);

            bool transferred = HairSkinningUtility.TransferClosestVertexWeights(build.mesh, sourceMesh,
                out string warning);

            Assert.That(transferred, Is.True, warning);
            Assert.That(build.mesh.bindposes, Has.Length.EqualTo(1));
            Assert.That(build.mesh.boneWeights, Has.Length.EqualTo(build.mesh.vertexCount));
        }

        [Test]
        public void RuntimeApiGeneratesDisposableHairWithoutEditorServices()
        {
            using HairGroomRuntimeAPI.GeneratedHair generated = HairGroomRuntimeAPI.Generate(groom);

            Assert.That(generated.Mesh, Is.Not.Null);
            Assert.That(generated.Evaluation.CardCount, Is.EqualTo(1));
            Assert.That(generated.Build.triangleCount, Is.GreaterThan(0));
        }

        private static HairGuide CreateGuide(string name, int seed, Vector3 rootPosition)
        {
            HairGuide guide = new HairGuide
            {
                name = name,
                seed = seed,
                root = HairSurfaceAnchor.Create("mesh:test", 0, 0, new Vector3(0.25f, 0.25f, 0.5f),
                    0f, rootPosition, Vector3.up)
            };
            guide.points.Add(new HairGuidePoint { position = rootPosition, width = 0.04f });
            guide.points.Add(new HairGuidePoint { position = rootPosition + new Vector3(0.02f, 0.15f, 0f), width = 0.025f });
            guide.points.Add(new HairGuidePoint { position = rootPosition + new Vector3(0.05f, 0.3f, 0.02f), width = 0.005f });
            return guide;
        }
    }

    public sealed class HairGroomRecoveryTests
    {
        private string testFolder;
        private string recoveryPath;
        private HairGroomAsset persistentGroom;

        [SetUp]
        public void SetUp()
        {
            string folderName = "__HairGroomRecoveryTests_" + System.Guid.NewGuid().ToString("N");
            testFolder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            Mesh sourceMesh = new Mesh { name = "RecoverySource" };
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            sourceMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            sourceMesh.triangles = new[] { 0, 1, 2 };
            AssetDatabase.CreateAsset(sourceMesh, testFolder + "/RecoverySource.asset");

            persistentGroom = ScriptableObject.CreateInstance<HairGroomAsset>();
            persistentGroom.name = "PersistentHairGroom";
            persistentGroom.SetSource(sourceMesh, "asset:recovery-test", "TestRace", "TestHead");
            persistentGroom.BakeSettings.assetName = "SnapshotValue";
            AssetDatabase.CreateAsset(
                persistentGroom, testFolder + "/PersistentHairGroom.asset");
            AssetDatabase.SaveAssetIfDirty(persistentGroom);
            recoveryPath = "Assets/UMAProjectData/HairCards/Recovery/" +
                           persistentGroom.GroomId + ".asset";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(recoveryPath)) AssetDatabase.DeleteAsset(recoveryPath);
            if (!string.IsNullOrEmpty(testFolder)) AssetDatabase.DeleteAsset(testFolder);
        }

        [Test]
        public void SnapshotMainObjectNameMatchesStableRecoveryFilename()
        {
            HairGroomRecovery.SaveSnapshot(persistentGroom);
            HairGroomAsset snapshot =
                AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            string originalRecoveryGuid = AssetDatabase.AssetPathToGUID(recoveryPath);

            persistentGroom.BakeSettings.assetName = "SecondSnapshotValue";
            HairGroomRecovery.SaveSnapshot(persistentGroom);

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.name, Is.EqualTo(persistentGroom.GroomId));
            Assert.That(snapshot.GroomId, Is.EqualTo(persistentGroom.GroomId));
            Assert.That(
                (snapshot.hideFlags & HideFlags.NotEditable) != 0,
                Is.True);
            Assert.That(snapshot.BakeSettings.assetName, Is.EqualTo("SecondSnapshotValue"));
            Assert.That(AssetDatabase.AssetPathToGUID(recoveryPath), Is.EqualTo(originalRecoveryGuid));
        }

        [Test]
        public void RestorePreservesTargetFilenameAndRestoresGroomData()
        {
            HairGroomRecovery.SaveSnapshot(persistentGroom);
            HairGroomAsset snapshot =
                AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            persistentGroom.BakeSettings.assetName = "ModifiedAfterSnapshot";

            HairGroomRecovery.RestoreSnapshotData(persistentGroom, snapshot);

            Assert.That(persistentGroom.name, Is.EqualTo("PersistentHairGroom"));
            Assert.That(persistentGroom.BakeSettings.assetName, Is.EqualTo("SnapshotValue"));
            Assert.That(
                AssetDatabase.GetAssetPath(persistentGroom),
                Is.EqualTo(testFolder + "/PersistentHairGroom.asset"));
            Assert.That(persistentGroom.hideFlags, Is.EqualTo(HideFlags.None));
        }

        [Test]
        public void RepairCorrectsLegacyRecoveryMainObjectName()
        {
            HairGroomRecovery.SaveSnapshot(persistentGroom);
            HairGroomAsset snapshot =
                AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            snapshot.name = "PersistentHairGroom Recovery";
            EditorUtility.SetDirty(snapshot);

            int repaired = HairGroomRecovery.RepairSnapshotNames();

            Assert.That(repaired, Is.GreaterThanOrEqualTo(1));
            Assert.That(snapshot.name, Is.EqualTo(persistentGroom.GroomId));
        }
    }
}
#endif
