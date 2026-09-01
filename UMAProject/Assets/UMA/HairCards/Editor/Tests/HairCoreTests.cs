#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

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
            Assert.That(build.triangleCount, Is.EqualTo(12));
            Assert.That(build.mesh.vertexCount, Is.EqualTo(build.vertexCount));
            Assert.That(build.mesh.uv, Has.Length.EqualTo(build.vertexCount));
            Assert.That(build.mesh.normals, Has.Length.EqualTo(build.vertexCount));
            Assert.That(build.degenerateTriangleCount, Is.Zero);
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
}
#endif
