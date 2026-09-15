#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairModifierSettings SetUpRinglet()
        {
            var group = groom.Groups[0]; group.guides.Clear(); group.children.childrenPerGuide = 0;
            var guide = new HairGuide { name = "Ringlet centerline", seed = 813,
                root = HairSurfaceAnchor.Create("mesh:test", 0, 0, new Vector3(.25f, .25f, .5f), 0, Vector3.zero, Vector3.forward) };
            for (int i = 0; i < 5; i++) guide.points.Add(new HairGuidePoint { position = Vector3.up * (.05f * i), width = .006f, widthBaseline = .006f });
            guide.EnsureIntegrity(.006f); group.guides.Add(guide);
            var modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Ringlets);
            modifier.amount = .01f; modifier.rootInfluence = 1f;
            modifier.ringlets.radiusVariation = modifier.ringlets.turnVariation = modifier.ringlets.phaseVariation = modifier.ringlets.reverseFraction = 0;
            modifier.ringlets.tipRadius = 1f;
            profile.Configure(HairCardShape.Ribbon, .006f, .002f, 64, generateBackfaces: false);
            return modifier;
        }
        private HairEvaluatedCurve RingletResult() => HairGroomEvaluator.Evaluate(groom,
            new HairEvaluationOptions { evaluateSurfaceAnchors = false }).evaluatedGuides[0];

        [Test]
        public void RingletGeneratesRealTurnsWithoutMovingRootOrCenterlineEnvelope()
        {
            var modifier = SetUpRinglet();
            var result = RingletResult();
            Assert.That(result.points.Count, Is.GreaterThanOrEqualTo(37));
            Assert.That(result.points[0].position, Is.EqualTo(Vector3.zero));
            Assert.That(result.points[^1].position.y, Is.EqualTo(.2f).Within(1e-6f));
            Assert.That(result.Length, Is.GreaterThan(.25f));
            foreach (var p in result.points.Where(p => p.position.y > .04f))
                Assert.That(new Vector2(p.position.x, p.position.z).magnitude, Is.EqualTo(.01f).Within(1e-5f));
            Assert.That(groom.Groups[0].guides[0].points.Count, Is.EqualTo(5));
        }

        [Test]
        public void RingletLengthModePreservesArcLengthAndAnchors()
        {
            var modifier = SetUpRinglet(); modifier.ringlets.lengthMode = HairRingletLengthMode.PreserveStrandLength;
            var result = RingletResult();
            Assert.That(result.Length, Is.EqualTo(.2f).Within(2e-5f));
            Assert.That(result.points[0].position, Is.EqualTo(Vector3.zero));
            Assert.That(result.points[^1].position.y, Is.LessThan(.19f));
        }

        [TestCase(HairRingletLengthMode.KeepEnvelope)]
        [TestCase(HairRingletLengthMode.PreserveStrandLength)]
        public void RingletResamplingRetainsExactFrozenPoints(HairRingletLengthMode mode)
        {
            var modifier = SetUpRinglet(); modifier.ringlets.lengthMode = mode;
            var guide = groom.Groups[0].guides[0]; guide.points[2].freeze = 1;
            var result = RingletResult();
            Assert.That(result.points.Any(p => p.freeze == 1f && p.position == guide.points[2].position), Is.True);
            Assert.That(result.points.All(p => float.IsFinite(p.position.sqrMagnitude)), Is.True);
        }

        [Test]
        public void RingletProtectsRootsAndIsDeterministicAcrossEvaluationAndSamplingChanges()
        {
            var modifier = SetUpRinglet(); modifier.ringlets.turnVariation = .3f; modifier.ringlets.phaseVariation = 1f;
            var full = RingletResult();
            profile.Configure(HairCardShape.Ribbon, .006f, .002f, 8, generateBackfaces: false);
            var low = RingletResult();
            Assert.That(low.points.Select(p => p.position), Is.EqualTo(full.points.Select(p => p.position)));
            modifier.rootInfluence = 0;
            var protectedRoot = RingletResult();
            Assert.That(Mathf.Abs(protectedRoot.points[1].position.x) + Mathf.Abs(protectedRoot.points[1].position.z),
                Is.LessThan(Mathf.Abs(full.points[1].position.x) + Mathf.Abs(full.points[1].position.z)));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void RingletBypassZeroRadiusZeroMaskAndFullFreezeAreNoOps(int mode)
        {
            var modifier = SetUpRinglet();
            if (mode == 0) modifier.enabled = false;
            if (mode == 1) modifier.amount = 0;
            if (mode == 2) modifier.maskMapId = "not-a-map";
            if (mode == 3) foreach (var p in groom.Groups[0].guides[0].points) p.freeze = 1;
            var result = RingletResult();
            Assert.That(result.points.Select(p => p.position), Is.EqualTo(groom.Groups[0].guides[0].points.Select(p => p.position)));
        }

        [Test]
        public void RingletSettingsPersistAndDuplicateIndependently()
        {
            var modifier = SetUpRinglet(); modifier.ringlets.turns = 7; modifier.ringlets.tipRadius = .25f;
            var duplicate = modifier.Duplicate(); duplicate.ringlets.turns = 2;
            Assert.That(modifier.ringlets.turns, Is.EqualTo(7));
            var reload = JsonUtility.FromJson<HairModifierSettings>(JsonUtility.ToJson(modifier));
            Assert.That(reload.ringlets.tipRadius, Is.EqualTo(.25f));
            reload.ringlets.turns = float.NaN; reload.ringlets.pointsPerTurn = int.MaxValue;
            reload.EnsureIntegrity(); Assert.That(float.IsFinite(reload.ringlets.turns), Is.True);
            Assert.That(reload.ringlets.pointsPerTurn, Is.EqualTo(20));
        }

        [Test]
        public void RingletDistanceSpacingAddsTurnsForLongerStrands()
        {
            var modifier = SetUpRinglet(); modifier.ringlets.spacingMode = HairRingletSpacingMode.DistanceBetweenTurns;
            modifier.ringlets.turnSpacing = .05f;
            var shortHair = RingletResult();
            foreach (var p in groom.Groups[0].guides[0].points) p.position *= 2;
            var longHair = RingletResult();
            Assert.That(shortHair.points.Count, Is.EqualTo(49));
            Assert.That(longHair.points.Count, Is.EqualTo(97));
            Assert.That(longHair.points[0].position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void RingletPhaseFrameAvoidsHalfTwistsOnStraightCenterlines()
        {
            var modifier = SetUpRinglet(); modifier.ringlets.phaseVariation = 1;
            groom.Lods[0].useProfileSamples = true;
            for (int seed = 0; seed < 16; seed++)
            {
                modifier.seed = seed;
                var evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
                using var build = HairCardMeshGenerator.Build(evaluation);
                Assert.That(build.frameFlipCount, Is.Zero, "Unexpected ribbon half-twist for phase seed " + seed);
            }
        }

        [Test]
        public void RingletHighResolutionProducesFiniteNormalsAndRespectsHardCap()
        {
            var modifier = SetUpRinglet(); modifier.ringlets.turns = 12; modifier.ringlets.pointsPerTurn = 20;
            modifier.ringlets.turnVariation = .75f;
            profile.Configure(HairCardShape.Ribbon, .002f, .001f, 256, generateBackfaces: false);
            groom.Lods[0].useProfileSamples = true;
            var evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            Assert.That(evaluation.evaluatedGuides[0].points.Count, Is.LessThanOrEqualTo(256));
            Assert.That(profile.SamplesPerCard, Is.EqualTo(256));
            using var build = HairCardMeshGenerator.Build(evaluation);
            Assert.That(build.vertexCount, Is.GreaterThan(128));
            Assert.That(build.mesh.normals.All(n => float.IsFinite(n.sqrMagnitude) && n.sqrMagnitude > .9f), Is.True);
        }

        [Test]
        public void RingletPresetPreservesAuthoredHairAndSharedMaterial()
        {
            SetUpRinglet(); var group = groom.Groups[0];
            var guides = group.guides; var layers = group.sculptLayers; var maps = group.maps;
            group.atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>(); group.atlas.EnsureIntegrity();
            var material = new Material(Shader.Find(HairSweptShaderGUI.ShaderName)); group.atlas.material = material;
            material.SetFloat("_SpecularStrength", 1.2f);
            HairCardProfileAsset generated = null;
            try
            {
                HairGenerationEditor.ApplyCurlyPreset(groom, group); generated = group.profile;
                Assert.That(group.guides, Is.SameAs(guides)); Assert.That(group.sculptLayers, Is.SameAs(layers));
                Assert.That(group.maps, Is.SameAs(maps)); Assert.That(group.atlas.material, Is.SameAs(material));
                Assert.That(material.GetFloat("_SpecularStrength"), Is.EqualTo(1.2f));
                Assert.That(group.generation.cards.modifiers.Count(m => m.type == HairModifierType.Ringlets), Is.EqualTo(1));
                Assert.That(group.profile.SamplesPerCard, Is.GreaterThanOrEqualTo(48));
                Assert.That(groom.Lods[0].useProfileSamples, Is.True);
            }
            finally { group.atlas.material = null; Object.DestroyImmediate(group.atlas); Object.DestroyImmediate(material); if (generated != null) Object.DestroyImmediate(generated); }
        }

        [Test]
        public void HighResolutionProfilesRetainLongRootVertexColorHolds()
        {
            profile.Configure(HairCardShape.Ribbon, .006f, .002f, 256, generateBackfaces: false);
            profile.ConfigureVertexColors(true, new Color(0, 0, 0, 0), Color.white, 120);
            Assert.That(profile.RootColorSegments, Is.EqualTo(120));
            Assert.That(profile.EvaluateVertexColor(120, 256, Color.white), Is.EqualTo(new Color(0, 0, 0, 0)));
            Assert.That(profile.EvaluateVertexColor(255, 256, Color.clear), Is.EqualTo(Color.white));
            Assert.That(profile.EvaluateVertexColor(11, 12, Color.clear), Is.EqualTo(Color.white), "Low LOD must still reach the tip RGBA.");
        }

        [Test]
        public void MatteFinishChangesOnlyHighlightSettingsAndSupportsUndo()
        {
            var material = new Material(Shader.Find(HairSweptShaderGUI.ShaderName));
            try
            {
                material.SetColor("_TipColor", Color.red); material.SetFloat("_Cutoff", .42f);
                float original = material.GetFloat("_SpecularStrength");
                Undo.IncrementCurrentGroup(); HairSweptShaderGUI.ApplyFinish(material, HairSweptShaderGUI.Finish.Matte);
                Assert.That(material.GetFloat("_SpecularStrength"), Is.LessThan(original));
                Assert.That(material.GetFloat("_SecondaryStrength"), Is.LessThan(.1f));
                Assert.That(material.GetColor("_TipColor"), Is.EqualTo(Color.red)); Assert.That(material.GetFloat("_Cutoff"), Is.EqualTo(.42f));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(material.GetFloat("_SpecularStrength"), Is.EqualTo(original));
            }
            finally { Undo.ClearUndo(material); Object.DestroyImmediate(material); }
        }

        [Test]
        public void PackagedCurlyExampleHasEditableRingletsAndSelfContainedResources()
        {
            const string folder = "Assets/UMAProjectData/HairCards/Examples/CurlyVolume/";
            if (!System.IO.Directory.Exists(folder)) return; // Optional examples are not in source-only distributions.
            var example = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(folder + "Curly_HairGroom.asset");
            Assert.That(example, Is.Not.Null);
            Assert.That(example.SourceMesh, Is.Not.Null);
            Assert.That(example.SourceMesh.isReadable, Is.True);
            var group = example.Groups.Single();
            Assert.That(group.guides.Count, Is.EqualTo(90));
            Assert.That(group.generation.enabled, Is.True);
            var curls = group.generation.cards.modifiers.Single(m => m.type == HairModifierType.Ringlets);
            Assert.That(curls.enabled, Is.True);
            Assert.That(curls.ringlets.spacingMode, Is.EqualTo(HairRingletSpacingMode.DistanceBetweenTurns));
            Assert.That(group.atlas.regions.Count, Is.EqualTo(3));
            Assert.That(group.atlas.regions.All(r => r.flipV), Is.True);
            Assert.That(group.atlas.material.shader.name, Is.EqualTo(HairSweptShaderGUI.ShaderName));
            Assert.That(group.atlas.material.GetFloat("_SpecularStrength"), Is.LessThan(.2f));
            Assert.That(ShaderUtil.ShaderHasError(group.atlas.material.shader), Is.False);
            foreach (Object resource in new Object[] { example.SourceMesh, group.profile, group.atlas,
                group.atlas.albedo, group.atlas.normal, group.atlas.mask, group.atlas.material })
                Assert.That(AssetDatabase.GetAssetPath(resource), Does.StartWith(folder), "Sample must not depend on another hairstyle's resources.");
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "GeneratedCards.asset");
            Assert.That(saved, Is.Not.Null);
            var evaluation = HairGroomEvaluator.Evaluate(example);
            using var build = HairCardMeshGenerator.Build(evaluation);
            Assert.That(build.cardCount, Is.GreaterThan(1000));
            Assert.That(build.degenerateTriangleCount, Is.Zero);
            Assert.That(saved.vertexCount, Is.EqualTo(build.vertexCount));
            var positions = saved.vertices; var expected = build.mesh.vertices;
            for (int i = 0; i < positions.Length; i++)
                Assert.That((positions[i] - expected[i]).sqrMagnitude, Is.LessThan(1e-10f), "Saved sample geometry must match its editable groom.");
        }
    }
}
#endif
