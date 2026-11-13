#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairGrowthMap TextureTestMap(int resolution = 16)
        {
            var map = groom.Groups[0].FindMap(HairMapKind.GrowthArea);
            HairGroomCommands.FillMap(groom, map, 0);
            HairGroomCommands.SetMapStorage(groom, map, HairMapStorage.Texture, resolution);
            return map;
        }
        private static float TextureAt(HairGrowthMap map, Vector3 bc) => map.SampleTriangle(0, 0, 0, 1, 2, bc);

        [TestCase(8)] [TestCase(16)] [TestCase(32)] [TestCase(64)]
        public void TextureTriangularFilterPreservesLinearFieldsAndNeverLeavesItsFace(int n)
        {
            var map = TextureTestMap(n); map.values = new[] { .1f, .9f, .4f };
            map.texture.EnsureTile(map, 0, 0, 0, 1, 2);
            for (int y = 0; y <= n * 2; y++) for (int x = 0; x <= n * 2 - y; x++)
            {
                var bc = HairTextureMap.TexelBarycentric(x, y, n * 2);
                Assert.That(TextureAt(map, bc), Is.EqualTo(.1f * bc.x + .9f * bc.y + .4f * bc.z).Within(1e-6));
                HairTextureMap.FilterCoordinates(bc, n, out int a, out int b, out int c, out var weights);
                foreach (int pixel in new[] { a, b, c }) Assert.That(pixel % (n + 1) + pixel / (n + 1), Is.LessThanOrEqualTo(n));
                Assert.That(weights.x + weights.y + weights.z, Is.EqualTo(1).Within(1e-6));
            }
        }

        [Test]
        public void TextureConversionIsSparseAndPreservesThePreviousMapExactly()
        {
            var map = groom.Groups[0].FindMap(HairMapKind.GrowthArea); map.values = new[] { .2f, .8f, .4f };
            var oldMesh = sourceMesh.vertices; var oldTriangles = sourceMesh.triangles; var anchor = groom.Groups[0].guides[0].root;
            HairGroomCommands.SetMapStorage(groom, map, HairMapStorage.Texture, 32);
            Assert.That(map.texture.tiles, Is.Empty);
            Assert.That(TextureAt(map, new Vector3(.2f, .3f, .5f)), Is.EqualTo(.48f).Within(1e-6));
            Assert.That(sourceMesh.vertices, Is.EqualTo(oldMesh)); Assert.That(sourceMesh.triangles, Is.EqualTo(oldTriangles));
            Assert.That(groom.Groups[0].guides[0].root, Is.EqualTo(anchor));
        }

        [Test]
        public void TinyTextureBrushPaintsInsideCoarseFaceAndGeneratesRootsThere()
        {
            var map = TextureTestMap(32); var surface = new HairTexturePaintSurface(sourceMesh);
            var center = new Vector3(0, 0, -.15f);
            Assert.That(surface.Paint(map, center, .09f, false, 1, p => (p - center).magnitude < .09f ? 1 : 0), Is.True);
            Assert.That(map.values, Is.EqualTo(new[] { 0f, 0f, 0f }));
            for (int i = 0; i < 3; i++) Assert.That(map.SampleVertex(i), Is.Zero);
            var result = HairGuideGenerator.Generate(groom, groom.Groups[0], new HairGuideGenerationSettings
                { guideCount = 100, minimumRootSpacing = 0, rootUniformity = 0, pointsPerGuide = 2 });
            Assert.That(result.guides.Count, Is.GreaterThan(20));
            foreach (var guide in result.guides)
            {
                Assert.That(guide.root.TriangleIndex, Is.Zero);
                Assert.That(TextureAt(map, guide.root.Barycentric), Is.GreaterThan(0));
                Assert.That(Vector3.Distance(guide.root.CachedLocalPosition, center), Is.LessThan(.13f));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void TexturePaintHardnessDoesNotAccumulateWithinOneStroke(bool erase)
        {
            var map = TextureTestMap(); var surface = new HairTexturePaintSurface(sourceMesh);
            if (erase) HairGroomCommands.FillMap(groom, map, 1);
            var center = Vector3.zero;
            for (int i = 0; i < 40; i++) surface.Paint(map, center, .3f, false, erase ? 0 : 1, p => .5f);
            Assert.That(TextureAt(map, new Vector3(.25f, .25f, .5f)), Is.EqualTo(.5f).Within(1e-6));
            surface.EndStroke(); surface.Paint(map, center, .3f, false, erase ? 0 : 1, p => .5f);
            Assert.That(TextureAt(map, new Vector3(.25f, .25f, .5f)), Is.EqualTo(erase ? .25f : .75f).Within(1e-6));
        }

        [Test]
        public void TextureBrushMirrorsInSourceSpaceAndRespectsLockedOrHiddenFaces()
        {
            var map = TextureTestMap(32); var surface = new HairTexturePaintSurface(sourceMesh);
            var center = new Vector3(.2f, 0, -.25f);
            float Brush(Vector3 p) => HairBrushInteractionUtility.EvaluateMirroredFalloff(p, center, .1f, 1, true);
            Assert.That(surface.Paint(map, center, .1f, true, 1, Brush, _ => false), Is.False);
            Assert.That(map.texture.tiles, Is.Empty); map.locked = true;
            Assert.That(surface.Paint(map, center, .1f, true, 1, Brush), Is.False); map.locked = false;
            surface.Paint(map, center, .1f, true, 1, Brush);
            var vertices = sourceMesh.vertices;
            foreach (var p in new[] { center, new Vector3(-center.x, center.y, center.z) })
                Assert.That(TextureAt(map, HairMeshUtility.Barycentric(p, vertices[0], vertices[1], vertices[2])), Is.GreaterThan(.95f));
        }

        [Test]
        public void TextureFillInvertResizeAndClipboardKeepDetailedData()
        {
            var map = TextureTestMap(); map.texture.EnsureTile(map, 0, 0, 0, 1, 2).pixels[4 * 17 + 4] = .8f; map.texture.Touch();
            var at = new Vector3(.5f, .25f, .25f); var clipboard = new HairGrowthMapClipboard();
            Assert.That(clipboard.TryCopy(groom, groom.Groups[0], map, true, out _), Is.True);
            Assert.That(map.texture.tiles, Is.Empty);
            clipboard = new HairGrowthMapClipboard(clipboard.Serialize());
            Assert.That(clipboard.TryPaste(groom, groom.Groups[0], map, out _), Is.True);
            Assert.That(TextureAt(map, at), Is.EqualTo(.8f));
            map.texture.Resize(32); Assert.That(TextureAt(map, at), Is.EqualTo(.8f));
            map.texture.Resize(16); Assert.That(TextureAt(map, at), Is.EqualTo(.8f));
            HairGroomCommands.InvertMap(groom, map); Assert.That(TextureAt(map, at), Is.EqualTo(.2f).Within(1e-6));
            HairGroomCommands.FillMap(groom, map, .3f); Assert.That(map.texture.tiles, Is.Empty); Assert.That(TextureAt(map, at), Is.EqualTo(.3f).Within(1e-6));
            clipboard.TryPaste(groom, groom.Groups[0], map, out _); Assert.That(TextureAt(map, at), Is.EqualTo(.8f), "Clipboard never aliases live texels.");
        }

        [Test]
        public void TextureSmoothSpreadsAnInteriorDotWithoutTouchingTheSourceMesh()
        {
            var map = TextureTestMap(); var tile = map.texture.EnsureTile(map, 0, 0, 0, 1, 2);
            tile.pixels[4 * 17 + 4] = 1; map.texture.Touch(); var original = sourceMesh.vertices;
            HairGroomCommands.SmoothMap(groom, map);
            Assert.That(tile.pixels[4 * 17 + 4], Is.EqualTo(1f / 7).Within(1e-6));
            Assert.That(tile.pixels[4 * 17 + 5], Is.GreaterThan(0)); Assert.That(sourceMesh.vertices, Is.EqualTo(original));
        }

        [Test]
        public void TextureFieldTracksInteriorMaskEditsAndDoesNotMistakeRefinementForAMeshBorder()
        {
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            sourceMesh.triangles = new[] { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2 }; sourceMesh.RecalculateNormals();
            groom.SetSource(sourceMesh, "mesh:test", "TestRace", "TestScalp");
            var map = TextureTestMap(); HairGroomCommands.FillMap(groom, map, 1);
            map.texture.EnsureTile(map, 0, 0, 0, 1, 2);
            var fields = new HairSurfaceFields(); fields.Prepare(sourceMesh, groom.Groups[0]); int revision = fields.Revision;
            var anchor = HairSurfaceAnchor.Create("mesh:test", 0, 0, new Vector3(.5f, .25f, .25f), 0, Vector3.zero, Vector3.forward);
            Assert.That(fields.Distance(anchor), Is.GreaterThan(999), "A refined face is not an open boundary on a closed surface.");
            map.texture.tiles[0].pixels[4 * 17 + 4] = 0; map.texture.Touch(); fields.Prepare(sourceMesh, groom.Groups[0]);
            Assert.That(fields.Revision, Is.GreaterThan(revision)); Assert.That(fields.Sample(map, anchor), Is.Zero);
            Assert.That(fields.Distance(anchor), Is.Zero.Within(1e-6));
        }

        [Test]
        public void TextureMapSurvivesNativeSaveAndReloadWithOriginalTopology()
        {
            string path = "Assets/HairTextureSaveTest-" + Guid.NewGuid().ToString("N") + ".asset";
            var saved = Object.Instantiate(groom); var mesh = Object.Instantiate(sourceMesh);
            try
            {
                AssetDatabase.CreateAsset(saved, path); AssetDatabase.AddObjectToAsset(mesh, saved);
                saved.SetSource(mesh, "mesh:saved", "TestRace", "TestScalp");
                var map = saved.Groups[0].FindMap(HairMapKind.GrowthArea);
                HairGroomCommands.SetMapStorage(saved, map, HairMapStorage.Texture, 32);
                map.texture.EnsureTile(map, 0, 0, 0, 1, 2).pixels[8 * 33 + 8] = .37f;
                EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved); Resources.UnloadAsset(saved);
                saved = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(path); map = saved.Groups[0].FindMap(HairMapKind.GrowthArea);
                Assert.That(map.UsesTexture, Is.True); Assert.That(map.texture.resolution, Is.EqualTo(32));
                Assert.That(TextureAt(map, new Vector3(.5f, .25f, .25f)), Is.EqualTo(.37f));
                Assert.That(map.texture.IsValid(saved.SourceTopologySignature, 3), Is.True);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [Test]
        public void TextureMapUndoRestoresPixelsAndInvalidatesFieldCaches()
        {
            var map = TextureTestMap(); map.texture.EnsureTile(map, 0, 0, 0, 1, 2); map.texture.Touch();
            Undo.ClearAll(); Undo.RegisterCompleteObjectUndo(groom, "Paint test texel");
            int before = map.TextureRevision; map.texture.tiles[0].pixels[4 * 17 + 4] = 1; map.texture.Touch();
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); map = groom.Groups[0].FindMap(HairMapKind.GrowthArea);
            Assert.That(TextureAt(map, new Vector3(.5f, .25f, .25f)), Is.Zero);
            Assert.That(map.TextureRevision, Is.Not.EqualTo(before)); Undo.ClearAll();
        }

        [Test]
        public void TexturePreviewUsesDedicatedCoordinatesAndOneSourceFaceWithoutSubdivision()
        {
            var map = TextureTestMap(); map.texture.EnsureTile(map, 0, 0, 0, 1, 2).pixels[4 * 17 + 4] = 1; map.texture.Touch();
            using var preview = new HairTextureMapPreview(); preview.Update(sourceMesh, sourceMesh, map, _ => true, true);
            Assert.That(preview.Material.shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(preview.Material.shader), Is.Empty);
            Assert.That(preview.Mesh.vertexCount, Is.EqualTo(3)); Assert.That(preview.Mesh.triangles.Length, Is.EqualTo(3));
            Assert.That(preview.Texture.GetPixel(4, 4).r, Is.EqualTo(1));
            var coords = new List<Vector4>(); preview.Mesh.GetUVs(0, coords);
            Assert.That(coords[1].x, Is.EqualTo(1)); Assert.That(coords[2].y, Is.EqualTo(1));
        }

        [Test]
        public void TextureChildCountSamplesAtTheRootRatherThanTheThreeSourceVertices()
        {
            var group = groom.Groups[0]; group.children.childrenPerGuide = 6;
            var map = HairGroomCommands.EnsureMap(groom, group, HairMapKind.ChildCount); HairGroomCommands.FillMap(groom, map, 0);
            HairGroomCommands.SetMapStorage(groom, map, HairMapStorage.Texture);
            var tile = map.texture.EnsureTile(map, 0, 0, 0, 1, 2); tile.pixels[8 * 17 + 4] = .5f; map.texture.Touch();
            Assert.That(HairGroomEvaluator.Evaluate(groom).childCurveCount, Is.EqualTo(3));
            Assert.That(map.values, Is.EqualTo(new[] { 0f, 0f, 0f }));
        }

        [Test]
        public void TextureSmoothingWeldsDuplicatedUVSeamTexels()
        {
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward, Vector3.right, Vector3.zero, Vector3.back * 2 };
            sourceMesh.triangles = new[] { 0, 1, 2, 3, 4, 5 }; sourceMesh.RecalculateNormals(); groom.SetSource(sourceMesh, "mesh:test");
            var map = TextureTestMap(); var a = map.texture.EnsureTile(map, 0, 0, 0, 1, 2);
            a.pixels[4] = 1; map.texture.Touch(); HairGroomCommands.SmoothMap(groom, map);
            var b = map.texture.Find(0, 1); Assert.That(b, Is.Not.Null);
            for (int x = 0; x <= 16; x++) Assert.That(a.pixels[x], Is.EqualTo(b.pixels[16 - x]).Within(1e-6));
        }

        [Test]
        public void TextureScalpVertexShadingNeverBleedsOutsideAPartiallyPaintedFace()
        {
            var map = TextureTestMap(); HairGroomCommands.FillMap(groom, map, 1);
            var tile = map.texture.EnsureTile(map, 0, 0, 0, 1, 2); tile.pixels[4 * 17 + 4] = 0; map.texture.Touch();
            var fields = new HairSurfaceFields(); fields.Prepare(sourceMesh, groom.Groups[0]);
            for (int i = 0; i < 3; i++) Assert.That(fields.PaintedDensity(i), Is.Zero);
            Assert.That(map.SampleVertex(0), Is.EqualTo(1), "Conservative shading must not erase the growth field.");
        }

        [Test]
        public void TextureInvalidTopologyStopsGenerationAndReportsAnActionableValidationError()
        {
            var map = TextureTestMap(); map.texture.topology = "different-source"; map.texture.Touch();
            Assert.That(HairGroomEvaluator.Evaluate(groom).curves, Is.Empty);
            Assert.That(HairGuideGenerator.Generate(groom, groom.Groups[0], new HairGuideGenerationSettings()).guides, Is.Empty);
            Assert.That(HairValidator.Validate(groom).issues.Exists(i => i.code == HairValidationCode.InvalidTextureMap), Is.True);
        }

        [Test]
        public void TextureStagePaintAndMapSelectionReachAnInteriorOnlyPatch()
        {
            var map = TextureTestMap(32); var stage = CreateEditingStage();
            try
            {
                stage.SceneTool = HairSceneTool.PaintGrowth; stage.BrushRadius = .1f; stage.BrushHardness = 1; stage.BrushStrength = 1;
                stage.PaintValue = 1; stage.MirrorPaintX = false;
                InvokeStageMethod(stage, "BeginStroke", "Interior texture stroke"); InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                InvokeStageMethod(stage, "EndStroke");
                Assert.That(TextureAt(map, new Vector3(.25f,.25f,.5f)), Is.EqualTo(1));
                stage.SelectFromActiveMap(); Assert.That(stage.SelectedVertexCount, Is.EqualTo(3));
                stage.GetGrowthAreaStatistics(out int painted, out _, out float maximum); Assert.That(painted, Is.GreaterThan(0)); Assert.That(maximum, Is.EqualTo(1));
                Assert.That(stage.TryGetCurrentAreaBounds(out _, out _), Is.True);
                stage.FillVisibleActiveMap(0); Assert.That(TextureAt(map, new Vector3(.25f,.25f,.5f)), Is.Zero);
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void NewGroomInheritsTextureSettingsButNotItsPaint()
        {
            string path = "Assets/HairTextureDefaultsTest-" + Guid.NewGuid().ToString("N") + ".asset";
            var preferences = HairEditorPreferences.instance; string savedPreferences = JsonUtility.ToJson(preferences);
            var saved = Object.Instantiate(groom); var next = ScriptableObject.CreateInstance<HairGroomAsset>();
            try
            {
                AssetDatabase.CreateAsset(saved, path); saved.Groups[0].profile = null; saved.Groups[0].atlas = null;
                var map = saved.Groups[0].FindMap(HairMapKind.GrowthArea);
                HairGroomCommands.SetMapStorage(saved, map, HairMapStorage.Texture, 32);
                map.texture.EnsureTile(map, 0, 0, 0, 1, 2).pixels[8 * 33 + 8] = 1; map.texture.Touch();
                HairEditorPreferences.Suspended = false; preferences.RememberSetup(saved, saved.Groups[0]);
                next.SetSource(sourceMesh, "mesh:new-groom"); preferences.ApplySetupToNewGroom(next);
                var inherited = next.Groups[0].FindMap(HairMapKind.GrowthArea);
                Assert.That(inherited.UsesTexture, Is.True); Assert.That(inherited.texture.resolution, Is.EqualTo(32));
                Assert.That(inherited.texture.tiles, Is.Empty); Assert.That(inherited.texture.topology, Is.EqualTo(next.SourceTopologySignature));
                Assert.That(inherited.values, Is.EqualTo(new[] { 0f, 0f, 0f }));
            }
            finally
            {
                HairEditorPreferences.Suspended = true; JsonUtility.FromJsonOverwrite(savedPreferences, preferences); preferences.Flush();
                AssetDatabase.DeleteAsset(path); Object.DestroyImmediate(next);
            }
        }
    }
}
#endif
