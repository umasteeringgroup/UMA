#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [Test]
        public void RegularPresetPreservesAuthoredWorkResourcesAndOtherGroups()
        {
            var group = groom.Groups[0]; var other = groom.CreateGroup("Untouched", HairGroupRole.Coverage);
            var otherState = JsonUtility.ToJson(other);
            var guides = group.guides; var maps = group.maps; var layers = group.sculptLayers;
            bool useProfileSamples = groom.Lods[0].useProfileSamples;
            var atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>(); atlas.EnsureIntegrity();
            var material = new Material(Shader.Find(HairSweptShaderGUI.ShaderName));
            var region = atlas.CreateRegion("Keep me", new Rect(.1f, .2f, .2f, .3f));
            atlas.material = material; group.atlas = atlas; material.SetFloat("_SpecularStrength", .83f);
            HairCardProfileAsset created = null;
            try
            {
                Undo.IncrementCurrentGroup();
                HairGenerationEditor.ApplyRegularPreset(groom, group); created = group.profile;
                Assert.That(group.guides, Is.SameAs(guides)); Assert.That(group.maps, Is.SameAs(maps)); Assert.That(group.sculptLayers, Is.SameAs(layers));
                Assert.That(group.atlas, Is.SameAs(atlas)); Assert.That(atlas.regions[0], Is.SameAs(region));
                Assert.That(atlas.material, Is.SameAs(material)); Assert.That(material.GetFloat("_SpecularStrength"), Is.EqualTo(.83f));
                Assert.That(JsonUtility.ToJson(other), Is.EqualTo(otherState));
                Assert.That(groom.Lods[0].useProfileSamples, Is.EqualTo(useProfileSamples), "Applying to one group must preserve groom-wide LOD overrides.");
                Assert.That(group.profile.SamplesPerCard, Is.EqualTo(5)); Assert.That(group.profile.RibbonSpans, Is.EqualTo(1));
                Assert.That(group.profile.DoubleSided, Is.False); Assert.That(group.profile.AdaptiveSampling, Is.False);
                Assert.That(group.generation.cards.count * (group.profile.SamplesPerCard - 1) * 2, Is.EqualTo(20000));
                Assert.That(group.generation.cards.modifiers.Any(m => m.type == HairModifierType.Ringlets), Is.False);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(groom.Groups[0].profile, Is.SameAs(profile));
                Assert.That(groom.Groups[0].generation.enabled, Is.False);
            }
            finally { Undo.ClearUndo(groom); if (created != null) Object.DestroyImmediate(created); Object.DestroyImmediate(atlas); Object.DestroyImmediate(material); }
        }

        [Test]
        public void ShortAtlasStripsAreAdditiveIdempotentAndRootOriented()
        {
            var atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>(); atlas.EnsureIntegrity(); atlas.regions.Clear();
            try
            {
                var original = atlas.CreateRegion("Existing", new Rect(.6f,.1f,.1f,.8f));
                HairSweptAtlasSetup.AddRegularStrips(atlas); HairSweptAtlasSetup.AddRegularStrips(atlas);
                Assert.That(atlas.regions.Count, Is.EqualTo(4)); Assert.That(atlas.regions[0], Is.SameAs(original));
                foreach (var strip in atlas.regions.Skip(1))
                {
                    Assert.That(strip.flipV, Is.True); Assert.That(strip.uvRect.width, Is.GreaterThan(.07f));
                    Assert.That(strip.uvRect.height, Is.InRange(.18f,.19f));
                }
            }
            finally { Object.DestroyImmediate(atlas); }
        }

        [TestCase("ShortHairPart_HairGroom")]
        [TestCase("ShortHairPart_Relaxed_HairGroom")]
        [TestCase("ShortHairPart_CloseCut_HairGroom")]
        [TestCase("ShortHairPart_Natural_HairGroom")]
        [TestCase("ShortHairPart_FrontFlipNatural_HairGroom")]
        public void PackagedShortHairPartStaysWithinBudgetAndMatchesEditableGroom(string name)
        {
            const string folder = "Assets/UMAProjectData/HairCards/Examples/ShortHairPart/";
            if (!Directory.Exists(folder)) return; // Optional sample, but mandatory checks when installed.
            var example = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(folder + name + ".asset");
            Assert.That(example, Is.Not.Null); Assert.That(example.Groups.Count, Is.EqualTo(2));
            Assert.That(example.Groups.Sum(g => g.guides.Count), Is.EqualTo(39));
            Assert.That(example.Groups[0].generation.cards.Id, Is.Not.EqualTo(example.Groups[1].generation.cards.Id));
            Assert.That(example.SymmetryEnabled, Is.False, "A deliberate side part should not be mirrored by default.");
            Assert.That(HairSweptShaderGUI.IsHybrid(example.Groups[0].atlas), Is.True);
            foreach (var group in example.Groups)
            {
                var map = group.FindMap(HairMapKind.GrowthArea);
                Assert.That(map.UsesTexture, Is.True); Assert.That(map.texture.resolution, Is.EqualTo(32));
                Assert.That(map.texture.tiles.Count, Is.GreaterThan(0));
                Assert.That(map.texture.IsValid(example.SourceTopologySignature, example.SourceVertexCount), Is.True);
            }
            var workspace = new HairEvaluationWorkspace();
            int previousTriangles = int.MaxValue;
            for (int lod = 0; lod < 3; lod++)
            {
                var evaluation = HairGroomEvaluator.Evaluate(example, new HairEvaluationOptions { lodLevel = lod }, workspace);
                using var build = HairCardMeshGenerator.Build(evaluation);
                Assert.That(build.triangleCount, Is.InRange(1, 20000)); Assert.That(build.triangleCount, Is.LessThan(previousTriangles)); previousTriangles = build.triangleCount;
                Assert.That(build.degenerateTriangleCount, Is.Zero); Assert.That(build.frameFlipCount, Is.Zero);
                var surface = new HairMeshRaycaster(example.SourceMesh);
                Assert.That(example.Groups.All(g=>g.preventCardPenetration && g.rootEmbedDepth==0),Is.True);
                Assert.That(WorstCardDistance(build.mesh, surface, 4), Is.GreaterThan(-.0001f),
                    "Card vertex/edge/face is inside the scalp: " + name + " LOD " + lod);
                Assert.That(evaluation.populations.Sum(p => p.rejectedInfluences), Is.Zero);
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(folder + name + "_LOD" + lod + ".asset");
                Assert.That(saved, Is.Not.Null); Assert.That(saved.vertexCount, Is.EqualTo(build.vertexCount));
                Assert.That(saved.subMeshCount,Is.EqualTo(build.mesh.subMeshCount));
                for(int sub=0;sub<saved.subMeshCount;sub++)
                {
                    Assert.That(saved.GetIndices(sub),Is.EqualTo(build.mesh.GetIndices(sub)),"Saved pass indices differ at LOD "+lod);
                    if(sub>0)Assert.That(saved.GetSubMesh(sub).indexStart,Is.GreaterThanOrEqualTo(saved.GetSubMesh(sub-1).indexStart+saved.GetSubMesh(sub-1).indexCount));
                }
                var actual = saved.vertices; var expected = build.mesh.vertices;
                for (int i = 0; i < actual.Length; i++) Assert.That((actual[i] - expected[i]).sqrMagnitude, Is.LessThan(1e-10f));
                foreach (var curve in evaluation.curves)
                {
                    var group = example.FindGroup(curve.groupId);
                    Assert.That(group.guides.Any(g => g.Id == curve.parentGuideId), Is.True, "No guide influence may cross the part into another group.");
                }
            }
            foreach (var group in example.Groups)
                foreach (Object resource in new Object[] { example.SourceMesh, group.profile, group.atlas, group.atlas.material, group.atlas.secondPassMaterial, group.atlas.albedo, group.atlas.normal, group.atlas.mask })
                    Assert.That(AssetDatabase.GetAssetPath(resource), Does.StartWith(folder), "Sample resources must not depend on another hairstyle.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + "_Preview.prefab");
            Assert.That(prefab, Is.Not.Null); Assert.That(prefab.GetComponent<LODGroup>().GetLODs().Length, Is.EqualTo(3));
            foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                Assert.That(renderer.sharedMaterials,Is.EqualTo(new[]{example.Groups[0].atlas.material,example.Groups[0].atlas.secondPassMaterial}));
            Assert.That(ShaderUtil.ShaderHasError(example.Groups[0].atlas.material.shader), Is.False);
        }
    }
}
#endif
