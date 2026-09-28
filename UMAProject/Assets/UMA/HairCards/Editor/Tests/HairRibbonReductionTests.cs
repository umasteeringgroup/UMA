#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [Test]
        public void CurlMeshReductionReducesTrianglesWithoutChangingEvaluatedShape()
        {
            var modifier = SetUpRinglet(); groom.Lods[0].useProfileSamples = true;
            var evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            var curve = evaluation.curves[0]; var before = curve.points.ToArray();
            using var reduced = HairCardMeshGenerator.Build(evaluation);
            Assert.That(curve.points, Is.EqualTo(before));
            curve.ribbonReduction.enabled = false;
            using var uniform = HairCardMeshGenerator.Build(evaluation);
            Assert.That(reduced.triangleCount, Is.LessThan(uniform.triangleCount * .75f));
            Assert.That(reduced.frameFlipCount, Is.Zero);
            Assert.That(reduced.degenerateTriangleCount, Is.Zero);
            Assert.That(reduced.mesh.normals.All(n => float.IsFinite(n.sqrMagnitude) && n.sqrMagnitude > .99f), Is.True);
        }

        [Test]
        public void CurlMeshQualitySettingsDoNotChangeShapeAndDoNotLeakFromPooledCurves()
        {
            var modifier = SetUpRinglet(); var workspace = new HairEvaluationWorkspace(); var result = new HairEvaluationResult();
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairGroomEvaluator.EvaluateInto(groom, options, workspace, result);
            var before = result.curves[0].points.ToArray();
            modifier.ringlets.cardShapeError = .005f; modifier.ringlets.cardMaxSamples = 10;
            HairGroomEvaluator.EvaluateInto(groom, options, workspace, result);
            Assert.That(result.curves[0].points, Is.EqualTo(before));
            Assert.That(result.curves[0].ribbonReduction.maximumSamples, Is.EqualTo(10));
            var copy = result.curves[0].Clone(); copy.ribbonReduction.shapeError = .001f;
            Assert.That(result.curves[0].ribbonReduction.shapeError, Is.EqualTo(.005f));
            modifier.enabled = false;
            HairGroomEvaluator.EvaluateInto(groom, options, workspace, result);
            Assert.That(result.curves.All(c => !c.ribbonReduction.enabled), Is.True);
        }

        [Test]
        public void CurlRibbonBudgetHonorsLodCapsAndReportsUnmetErrorTargets()
        {
            var modifier = SetUpRinglet(); modifier.ringlets.cardMaxSamples = 64;
            groom.Lods[0].useProfileSamples = false; groom.Lods[0].samplesPerCard = 8;
            var result = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            using var mesh = HairCardMeshGenerator.Build(result, "Reduced", true);
            Assert.That(mesh.vertexCount, Is.EqualTo(16));
            Assert.That(mesh.samplingLimitedCardCount, Is.EqualTo(1));
            Assert.That(mesh.cards[0].samplingLimited, Is.True);
            var uv = new List<Vector2>(); mesh.mesh.GetUVs(1, uv);
            Assert.That(uv.Last().x, Is.EqualTo(1));
        }

        [Test]
        public void CurlRibbonParametersAndVertexRgbaFollowDistanceAfterReduction()
        {
            var modifier = SetUpRinglet(); groom.Lods[0].useProfileSamples = true;
            profile.ConfigureVertexColors(true, new Color(0, 0, 0, 0), Color.white, 2);
            var result = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            using var mesh = HairCardMeshGenerator.Build(result);
            var uv = new List<Vector2>(); mesh.mesh.GetUVs(1, uv); var colors = mesh.mesh.colors;
            Assert.That(uv[0].x, Is.EqualTo(0)); Assert.That(uv[^1].x, Is.EqualTo(1));
            Assert.That(colors[4], Is.EqualTo(Color.clear)); Assert.That(colors[^1], Is.EqualTo(Color.white));
            float hold = uv[4].x;
            for (int i = 6; i < colors.Length; i += 2)
                Assert.That(colors[i].a, Is.EqualTo((uv[i].x - hold) / (1f - hold)).Within(1e-5f));
            Assert.That(uv.Where((_, i) => i % 2 == 0).Select(v => v.x), Is.Ordered);
        }

        [Test]
        public void RibbonReductionKeepsTwistingEdgesEvenWithStraightCenterline()
        {
            profile.Configure(HairCardShape.Ribbon, .01f, .01f, 256, generateBackfaces: false);
            var curve = new HairEvaluatedCurve { profile = profile, rootNormal = Vector3.forward,
                ribbonReduction = new HairRibbonReductionSettings { enabled = true, maximumSamples = 256, shapeError = .001f, facingError = 18f } };
            for (int i = 0; i < 100; i++)
            {
                float t = i / 99f, angle = 6f * Mathf.PI * t;
                curve.points.Add(new HairCurvePoint(Vector3.up * (.2f * t), .01f, 0, .01f)
                { facingNormal = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)), facingWeight = 1 });
            }
            var evaluation = new HairEvaluationResult(); evaluation.curves.Add(curve);
            using var build = HairCardMeshGenerator.Build(evaluation);
            Assert.That(build.vertexCount, Is.GreaterThan(32), "Centerline-only simplification would lose all three ribbon twists.");
            Assert.That(build.vertexCount, Is.LessThan(180));
            Assert.That(build.frameFlipCount, Is.Zero); Assert.That(build.samplingLimitedCardCount, Is.Zero);
            var dense = curve.points;
            var tangents = new Vector3[100]; var sides = new Vector3[100]; var normals = new Vector3[100];
            HairCurveUtility.BuildRotationMinimizingFrames(dense, curve.rootNormal, tangents, sides, normals, out _);
            var vertices = build.mesh.vertices; var uv = new List<Vector2>(); build.mesh.GetUVs(1, uv);
            int row = 0;
            for (int i = 0; i < 100; i++)
            {
                float t = i / 99f;
                while (row + 2 < vertices.Length / 2 && uv[(row + 1) * 2].x < t) row++;
                float blend = Mathf.InverseLerp(uv[row * 2].x, uv[(row + 1) * 2].x, t);
                for (int edge = 0; edge < 2; edge++)
                {
                    var actual = Vector3.Lerp(vertices[row * 2 + edge], vertices[(row + 1) * 2 + edge], blend);
                    var expected = dense[i].position + sides[i] * (edge == 0 ? -.005f : .005f);
                    Assert.That(Vector3.Distance(actual, expected), Is.LessThanOrEqualTo(.00101f));
                }
            }
        }
    }
}
#endif
