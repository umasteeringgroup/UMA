using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public class UMASourceUVCroppingTests
    {
        private UMAResourceReuseAtlasTests.Fixture f;

        [SetUp]
        public void SetUp()
        {
            f = new UMAResourceReuseAtlasTests.Fixture();
            f.Generator.enableSourceUVCropping = true;
            f.Generator.sourceUVCropPadding = 4;
            f.Generator.fitAtlas = true;
            SetBounds(f.Meshes.Slot.meshData, new Rect(.25f, .25f, .25f, .25f));
        }

        [TearDown]
        public void TearDown()
        {
            if (f == null) return;
            foreach (var process in f.Generator.textureMerge.normalPostProcesses)
            {
                var material = typeof(UMAPostProcess).GetField("material", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(process) as Material;
                if (material != null) f.Meshes.Keep(material);
            }
            f.Dispose();
        }

        private static void SetBounds(UMAMeshData mesh, Rect bounds)
        {
            mesh.uv = new[] { bounds.min, new Vector2(bounds.xMax, bounds.yMin), bounds.max };
            mesh.InvalidateBuildData();
        }

        [Test]
        public void PreparedBoundsUpgradePersistAndInvalidateWithoutChangingSourceUVs()
        {
            var mesh = f.Meshes.Slot.meshData;
            var original = (Vector2[])mesh.uv.Clone();
            Assert.That(UMAMeshPreparation.TryGet(mesh, out var prepared), Is.True);
            Assert.That(prepared.TryGetSourceUVBounds(0, out var bounds), Is.True);
            Assert.That(bounds, Is.EqualTo(new Rect(.25f, .25f, .25f, .25f)));
            var reloaded = JsonUtility.FromJson<UMAMeshData>(JsonUtility.ToJson(mesh));
            var saved = reloaded.PreparedData;
            Assert.That(UMAMeshPreparation.TryGet(reloaded, out prepared), Is.True);
            Assert.That(prepared, Is.SameAs(saved));
            Assert.That(prepared.TryGetSourceUVBounds(0, out bounds), Is.True);
            CollectionAssert.AreEqual(original, mesh.uv);
            SetBounds(mesh, new Rect(.5f, .5f, .25f, .25f));
            Assert.That(UMAMeshPreparation.TryGet(mesh, out prepared), Is.True);
            Assert.That(prepared.TryGetSourceUVBounds(0, out bounds), Is.True);
            Assert.That(bounds.xMin, Is.EqualTo(.5f));
        }

        [Test]
        public void SharedRegionUsesUnionAndAnyParticipantCanVeto()
        {
            var avatar = f.Avatar(false);
            f.Build(avatar);
            var gm = avatar.generatedMaterials.materials[0];
            var first = gm.materialFragments[0];
            Assert.That(first.sourceUVRect, Is.EqualTo(new Rect(.125f, .125f, .5f, .5f)));
            var otherSlot = f.Meshes.Keep(ScriptableObject.CreateInstance<SlotDataAsset>());
            otherSlot.meshData = JsonUtility.FromJson<UMAMeshData>(JsonUtility.ToJson(f.Meshes.Slot.meshData));
            SetBounds(otherSlot.meshData, new Rect(.5f, .5f, .25f, .25f));
            var other = new UMAData.MaterialFragment { slotData = new SlotData(otherSlot), umaMaterial = gm.umaMaterial,
                baseOverlay = first.baseOverlay, overlayData = first.overlayData, isRectShared = true, rectFragment = first };
            gm.materialFragments.Add(other);
            UMASourceUVCropping.Prepare(gm, f.Generator, avatar);
            Assert.That(first.sourceUVRect, Is.EqualTo(new Rect(.125f, .125f, .75f, .75f)));
            Assert.That(other.sourceUVRect, Is.EqualTo(first.sourceUVRect));
            otherSlot.sourceUVCropping = UMASourceUVCropping.SlotMode.Disabled;
            UMASourceUVCropping.Prepare(gm, f.Generator, avatar);
            Assert.That(first.sourceUVRect, Is.EqualTo(UMASourceUVCropping.FullRect));
            Assert.That(other.sourceUVRect, Is.EqualTo(UMASourceUVCropping.FullRect));
            otherSlot.sourceUVCropping = UMASourceUVCropping.SlotMode.Automatic;
            SetBounds(otherSlot.meshData, new Rect(0, 0, 1, 1));
            UMASourceUVCropping.Prepare(gm, f.Generator, avatar);
            Assert.That(first.sourceUVRect, Is.EqualTo(UMASourceUVCropping.FullRect));
        }

        [TestCase("generator")] [TestCase("slot")] [TestCase("overlay")] [TestCase("tiledUV")]
        [TestCase("uvChannel")] [TestCase("placedSlot")] [TestCase("transform")] [TestCase("shader")]
        [TestCase("textureScale")] [TestCase("callback")]
        public void UnsupportedInputsUseFullSource(string reason)
        {
            var avatar = f.Avatar(false);
            switch (reason)
            {
                case "generator": f.Generator.enableSourceUVCropping = false; break;
                case "slot": f.Meshes.Slot.sourceUVCropping = UMASourceUVCropping.SlotMode.Disabled; break;
                case "overlay": f.Overlay.allowSourceUVCropping = false; break;
                case "tiledUV": SetBounds(f.Meshes.Slot.meshData, new Rect(0, 0, 2, 2)); break;
                case "uvChannel": avatar.umaRecipe.slotDataList[0].UVSet = 1; break;
                case "placedSlot": f.Meshes.Slot.useAtlasOverlay = true; break;
                case "transform": avatar.umaRecipe.slotDataList[0].GetOverlay(0).instanceTransformed = true; break;
                case "shader": f.Meshes.Material.material.shader = Shader.Find("Hidden/InternalErrorShader"); break;
                case "textureScale": f.Meshes.Material.material.SetTextureScale("_BaseMap", Vector2.one * 2); break;
                case "callback": f.Meshes.Slot.SlotAtlassed = new UMADataSlotMaterialRectEvent(); f.Meshes.Slot.SlotAtlassed.AddListener((a, b, c, d) => { }); break;
            }
            f.Build(avatar);
            Assert.That(avatar.generatedMaterials.materials[0].materialFragments[0].sourceUVRect, Is.EqualTo(UMASourceUVCropping.FullRect));
        }

        [TestCase(false)] [TestCase(true)]
        public void CroppedAtlasReusesOnlyMatchingCrops(bool reuse)
        {
            var a = f.Avatar(reuse); f.Build(a);
            var b = f.Avatar(reuse); f.Build(b);
            var atlasA = a.generatedMaterials.materials[0].resultingAtlasList[0];
            var atlasB = b.generatedMaterials.materials[0].resultingAtlasList[0];
            Assert.That(ReferenceEquals(atlasA, atlasB), Is.EqualTo(reuse));
            SetBounds(f.Meshes.Slot.meshData, new Rect(.5f, .5f, .25f, .25f));
            var c = f.Avatar(reuse); f.Build(c);
            Assert.That(c.generatedMaterials.materials[0].resultingAtlasList[0], Is.Not.SameAs(atlasA));
        }

        [TestCase(false)] [TestCase(true)]
        public void GeneratorKeepsDifferentSlotsOnOneSharedCroppedRectangle(bool veto)
        {
            var avatar = f.Avatar(false);
            var otherAsset = f.Meshes.Keep(ScriptableObject.CreateInstance<SlotDataAsset>());
            otherAsset.name = "Other crop slot";
            otherAsset.meshData = JsonUtility.FromJson<UMAMeshData>(JsonUtility.ToJson(f.Meshes.Slot.meshData));
            SetBounds(otherAsset.meshData, new Rect(.375f, .375f, .25f, .25f));
            if (veto) otherAsset.sourceUVCropping = UMASourceUVCropping.SlotMode.Disabled;
            var other = new SlotData(otherAsset);
            other.SetOverlayList(avatar.umaRecipe.slotDataList[0].GetOverlayList());
            avatar.umaRecipe.slotDataList = new[] { avatar.umaRecipe.slotDataList[0], other };
            f.Build(avatar);
            var fragments = avatar.generatedMaterials.materials[0].materialFragments;
            Assert.That(fragments.Count, Is.EqualTo(2));
            Assert.That(fragments.Count(x => x.isRectShared), Is.EqualTo(1));
            Assert.That(fragments[0].atlasRegion, Is.EqualTo(fragments[1].atlasRegion));
            Assert.That(fragments[0].sourceUVRect, Is.EqualTo(veto ? UMASourceUVCropping.FullRect : new Rect(.125f, .125f, .625f, .625f)));
            Assert.That(fragments[1].sourceUVRect, Is.EqualTo(fragments[0].sourceUVRect));
        }

        [Test]
        public void BoundsIgnoreUnreferencedVerticesButIncludeAllLODIndices()
        {
            var mesh = UMAMeshPreparationTests.Mesh();
            mesh.uv = Enumerable.Repeat(new Vector2(.25f, .25f), 12).ToArray();
            mesh.uv[1] = new Vector2(.5f, .5f);
            mesh.uv[4] = new Vector2(.75f, .75f);
            mesh.uv[11] = new Vector2(4, 4);
            mesh.submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2, 3, 4, 5 }) };
            Assert.That(UMAMeshPreparation.TryGet(mesh, out var prepared), Is.True);
            Assert.That(prepared.TryGetSourceUVBounds(0, out var bounds), Is.True);
            Assert.That(bounds, Is.EqualTo(new Rect(.25f, .25f, .5f, .5f)));
        }

        [Test]
        public void PreviousMetadataVersionAutomaticallyUpgrades()
        {
            var mesh = f.Meshes.Slot.meshData;
            mesh.PrepareBuildData();
            typeof(UMAMeshPreparation).GetField("version", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(mesh.PreparedData, 1);
            mesh.InvalidateBuildData();
            Assert.That(UMAMeshPreparation.TryGet(mesh, out var prepared), Is.True);
            Assert.That(prepared.Version, Is.EqualTo(UMAMeshPreparation.CurrentVersion));
            Assert.That(prepared.TryGetSourceUVBounds(0, out _), Is.True);
        }

        [Test]
        public void MeshReuseIncludesSourceCropEvenWhenPackedRectIsIdentical()
        {
            var a = f.Avatar(); f.Build(a); f.Meshes.Build(a);
            var b = f.Avatar(); f.Build(b);
            var af = a.generatedMaterials.materials[0].materialFragments[0];
            var bf = b.generatedMaterials.materials[0].materialFragments[0];
            Assert.That(bf.atlasRegion, Is.EqualTo(af.atlasRegion));
            bf.sourceUVRect = new Rect(.25f, .25f, .5f, .5f);
            f.Meshes.Build(b);
            Assert.That(b.GetRenderer(0).sharedMesh, Is.Not.SameAs(a.GetRenderer(0).sharedMesh));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void MeshBackendsRemapOriginalUVsIntoCrop(int backend)
        {
            var settings = UMASettings.GetSettingsFromResources();
            bool previous = settings.useMeshAPICombiner;
            try
            {
                var a = f.Avatar(false); a.reuseGeneratedMeshes = false; f.Build(a);
                if (backend == 0) f.Meshes.Build(a);
                else if (backend == 1) f.Meshes.BuildJobified(a);
                else
                {
                    settings.useMeshAPICombiner = backend == 3;
                    a.gameObject.AddComponent<UMADefaultMeshCombiner>().UpdateUMAMesh(true, a, 256);
                }
                var fragment = a.generatedMaterials.materials[0].materialFragments[0];
                var mapping = fragment.UncroppedAtlasRegion;
                var area = fragment.slotData.UVArea;
                Assert.That(Vector2.Distance(area.position, mapping.position / 256), Is.LessThan(.00001f));
                Assert.That(Vector2.Distance(area.size, mapping.size / 256), Is.LessThan(.00001f));
                var uv = a.GetRenderer(0).sharedMesh.uv;
                for (int i = 0; i < uv.Length; i++)
                {
                    var expected = (mapping.position + Vector2.Scale(mapping.size, f.Meshes.Slot.meshData.uv[i])) / 256;
                    Assert.That(Vector2.Distance(expected, uv[i]), Is.LessThan(.00001f));
                }
            }
            finally { settings.useMeshAPICombiner = previous; }
        }

        [Test]
        public void ClipUsesCorrectSourceOrientationAndNeverDrawsOutsideAllocation()
        {
            Assert.That(UMASourceUVCropping.ClipDraw(new Rect(0, 0, 100, 100), new Rect(25, 10, 50, 30), out var dest, out var uv), Is.True);
            Assert.That(dest, Is.EqualTo(new Rect(25, 10, 50, 30)));
            Assert.That(uv.x, Is.EqualTo(.25f).Within(.00001f));
            Assert.That(uv.y, Is.EqualTo(.6f).Within(.00001f));
            Assert.That(uv.width, Is.EqualTo(.5f).Within(.00001f));
            Assert.That(uv.height, Is.EqualTo(.3f).Within(.00001f));
            Assert.That(UMASourceUVCropping.ClipDraw(new Rect(200, 200, 10, 10), dest, out _, out _), Is.False);
        }

        [TestCase(UMAGeneratorBase.FitMethod.DecreaseResolution)]
        [TestCase(UMAGeneratorBase.FitMethod.BestFitSquare)]
        [TestCase(UMAGeneratorBase.FitMethod.MultipleHeuristics)]
        public void AllPackingModesPreserveCrop(UMAGeneratorBase.FitMethod mode)
        {
            f.Generator.AtlasOverflowFitMethod = mode;
            var a = f.Avatar(false); f.Build(a);
            Assert.That(a.generatedMaterials.materials[0].materialFragments[0].sourceUVRect,
                Is.EqualTo(new Rect(.125f, .125f, .5f, .5f)));
        }

        [Test]
        public void BoneBakingRemapsSourceUVsAndCachedArea()
        {
            var a = f.Avatar(false); f.Build(a);
            var combiner = a.gameObject.AddComponent<UMADefaultBoneBakingMeshCombiner>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(UMADefaultMeshCombiner).GetField("atlasResolution", flags).SetValue(combiner, 32);
            var mesh = new MeshBuilder { vertexCount = 3, has_uv = true, uv = (Vector2[])f.Meshes.Slot.meshData.uv.Clone() };
            typeof(UMADefaultBoneBakingMeshCombiner).GetMethod("RecalculateUV", flags, null,
                new[] { typeof(MeshBuilder), typeof(System.Collections.Generic.List<UMAData.GeneratedMaterial>), typeof(bool) }, null)
                .Invoke(combiner, new object[] { mesh, a.generatedMaterials.materials, true });
            var fragment = a.generatedMaterials.materials[0].materialFragments[0];
            for (int i = 0; i < 3; i++)
            {
                var mapping = fragment.UncroppedAtlasRegion;
                var expected = (mapping.position + Vector2.Scale(mapping.size, f.Meshes.Slot.meshData.uv[i])) / 32;
                Assert.That(Vector2.Distance(mesh.uv[i], expected), Is.LessThan(.00001f));
                Assert.That(Vector2.Distance(fragment.slotData.ConvertToAtlasUV(f.Meshes.Slot.meshData.uv[i]), expected), Is.LessThan(.00001f));
            }
        }

        [Test]
        public void TurningCroppingOffClearsReusedTextureCommands()
        {
            var a = f.Avatar(false); f.Build(a);
            f.Generator.enableSourceUVCropping = false;
            var b = f.Avatar(false); f.Build(b);
            var fragment = b.generatedMaterials.materials[0].materialFragments[0];
            Assert.That(fragment.sourceUVRect, Is.EqualTo(UMASourceUVCropping.FullRect));
            var image = f.Meshes.Keep(TextureMerge.GetRTPixels((RenderTexture)b.generatedMaterials.materials[0].resultingAtlasList[0]));
            Assert.That(image.width, Is.EqualTo(32));
            Assert.That(image.GetPixel(31, 31).a, Is.GreaterThan(.99f));
        }

        [TestCase(false, 0)] [TestCase(true, 0)] [TestCase(false, 1)] [TestCase(true, 1)]
        [TestCase(false, 2)] [TestCase(true, 2)]
        public void CroppedPixelsMatchFullAtlasIncludingPlacedOverlay(bool layered, int channel)
        {
            var merge = f.Generator.textureMerge;
            if (channel == 1)
            {
                merge.normalShader = Shader.Find("UMA/Atlas/AtlasShaderNormal");
                f.Meshes.Material.channels[0].channelType = UMAMaterial.ChannelType.NormalMap;
                var swizzle = f.Meshes.Keep(ScriptableObject.CreateInstance<UMAPostProcess>());
                swizzle.shader = Shader.Find("UMA/NormalSwizzleShader");
                merge.normalPostProcesses.Add(swizzle);
            }
            else if (channel == 2)
            {
                merge.dataShader = Shader.Find("UMA/Atlas/AtlasShaderNew");
                f.Meshes.Material.channels[0].channelType = UMAMaterial.ChannelType.Texture;
            }
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++) pixels[y * 32 + x] = new Color(x / 31f, y / 31f, .2f, 1);
            f.Source.SetPixels(pixels); f.Source.Apply();
            f.Source.filterMode = FilterMode.Point;
            var mask = f.Meshes.Keep(new Texture2D(32, 32, TextureFormat.RGBA32, false, true));
            mask.filterMode = FilterMode.Point;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1, 1, 1, (i / 32 + 1) / 32f);
            mask.SetPixels(pixels); mask.Apply();
            f.Overlay.alphaMask = mask;
            var plain = f.Avatar(false);
            var cropped = f.Avatar(false);
            if (layered)
            {
                var asset = f.Meshes.Keep(ScriptableObject.CreateInstance<OverlayDataAsset>());
                asset.material = f.Meshes.Material; asset.textureList = new Texture[] { f.Source };
                f.Generator.textureMerge.DiffuseBlendModeShaders.Add(new TextureMerge.BlendModeShaders { Combiner = f.Generator.textureMerge.diffuseShader });
                merge.NormalBlendModeShaders.Add(new TextureMerge.BlendModeShaders { Combiner = merge.normalShader });
                merge.DataBlendModeShaders.Add(new TextureMerge.BlendModeShaders { Combiner = merge.dataShader });
                foreach (var avatar in new[] { plain, cropped })
                {
                    var overlay = new OverlayData(asset) { rect = new Rect(6, 6, 10, 10) };
                    overlay.colorData.color = new Color(.7f, 1, .4f, .5f);
                    overlay.TransparentMultiplier = new Color(.2f, .3f, .4f, .2f);
                    avatar.umaRecipe.slotDataList[0].AddOverlay(overlay);
                }
            }
            f.Generator.enableSourceUVCropping = false; f.Build(plain);
            f.Generator.enableSourceUVCropping = true; f.Build(cropped);
            var full = f.Meshes.Keep(TextureMerge.GetRTPixels((RenderTexture)plain.generatedMaterials.materials[0].resultingAtlasList[0]));
            var cut = f.Meshes.Keep(TextureMerge.GetRTPixels((RenderTexture)cropped.generatedMaterials.materials[0].resultingAtlasList[0]));
            var fragment = cropped.generatedMaterials.materials[0].materialFragments[0];
            Assert.That(fragment.sourceUVRect, Is.Not.EqualTo(UMASourceUVCropping.FullRect));
            Assert.That(cut.width * cut.height, Is.LessThan(full.width * full.height));
            for (int y = 8; y < 16; y++)
                for (int x = 8; x < 16; x++)
                {
                    var uv = new Vector2((x + .5f) / 32, (y + .5f) / 32);
                    var map = fragment.UncroppedAtlasRegion;
                    var dest = (map.position + Vector2.Scale(map.size, uv)) / 32;
                    var expected = full.GetPixel(x, y);
                    var actual = cut.GetPixel(Mathf.FloorToInt(dest.x * cut.width), Mathf.FloorToInt(dest.y * cut.height));
                    Assert.That(Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g) + Mathf.Abs(actual.b - expected.b) + Mathf.Abs(actual.a - expected.a),
                        Is.LessThan(.025f), $"Source pixel {x},{y}");
                }
        }
    }
}
