#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMAGeneratorProFittingTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject root;
        private UMAGenerator generator;
        private UMAGeneratorPro fitter;
        private SlotDataAsset slot;
        private UMAMaterial materialAsset;
        private RenderTexture source;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Atlas fitting test") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            generator = root.AddComponent<UMAGenerator>();
            generator.enabled = false;
            generator.fitAtlas = true;
            generator.atlasResolution = 1024;
            fitter = new UMAGeneratorPro();
            SetField("umaGenerator", generator);
            SetField("packTexture", new MaxRectsBinPack(1024, 1024, false));
            slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.overlayScale = 1;
            materialAsset = ScriptableObject.CreateInstance<UMAMaterial>();
            materialAsset.materialType = UMAMaterial.MaterialType.Atlas;
            materialAsset.channels = new UMAMaterial.MaterialChannel[1];
            // Dimensions only; the render texture is never allocated on the GPU.
            source = new RenderTexture(2048, 2048, 0);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(slot);
            Object.DestroyImmediate(materialAsset);
        }

        private void SetField(string name, object value) => typeof(UMAGeneratorPro).GetField(name, Private).SetValue(fitter, value);
        private object Call(string name, params object[] args) => typeof(UMAGeneratorPro).GetMethod(name, Private).Invoke(fitter, args);
        private static object Area(int width, int height)
        {
            var type = typeof(UMAGeneratorPro).GetNestedType("SizeInt", BindingFlags.NonPublic);
            var value = Activator.CreateInstance(type);
            type.GetField("Width").SetValue(value, width);
            type.GetField("Height").SetValue(value, height);
            return value;
        }
        private UMAData.GeneratedMaterial Material(params Vector2Int[] sizes)
        {
            var material = new UMAData.GeneratedMaterial { umaMaterial = materialAsset, resolutionScale = Vector2.one };
            foreach (var size in sizes)
                material.materialFragments.Add(new UMAData.MaterialFragment {
                    slotData = new SlotData(slot) { overlayScale = 1 }, size = size.x * size.y,
                    sourceUVRect = new Rect(0, 0, size.x / 2048f, size.y / 2048f),
                    baseOverlay = new UMAData.textureData { textureList = new Texture[] { source } }
                });
            material.materialFragments.Sort(new UMAGeneratorPro.MaterialDefinitionComparer());
            return material;
        }
        private void Fit(UMAData.GeneratedMaterial material, UMAGeneratorBase.FitMethod method, int initialScale = 1)
        {
            material.resolutionScale = Vector2.one / initialScale;
            var args = new object[] { Area(1024, 1024), 1024f, Vector2.one, material };
            bool scaled = (bool)Call(method == UMAGeneratorBase.FitMethod.BestFitSquare ? "CalculateBestFitSquare" : "CalculateBestFit", args);
            if (scaled)
            {
                material.resolutionScale = (Vector2)args[2] / initialScale;
                Call("UpdateAtlasRects", material, args[2]);
            }
            Call("UpdateSharedRect", material);
        }
        private static void AssertValid(UMAData.GeneratedMaterial material, int width = 1024, int height = 1024)
        {
            var fragments = material.materialFragments.Where(f => !f.isRectShared && !f.isNoTextures).ToArray();
            for (int i = 0; i < fragments.Length; i++)
            {
                Rect rect = fragments[i].atlasRegion;
                Assert.That(rect.width, Is.GreaterThan(0), "Missing rectangle " + i);
                Assert.That(rect.height, Is.GreaterThan(0), "Missing rectangle " + i);
                Assert.That(rect.xMin, Is.InRange(0, width));
                Assert.That(rect.yMin, Is.InRange(0, height));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(width), "Right edge " + i);
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(height), "Top edge " + i);
                Assert.That(rect.x, Is.EqualTo(Mathf.Floor(rect.x)));
                Assert.That(rect.y, Is.EqualTo(Mathf.Floor(rect.y)));
                for (int j = 0; j < i; j++)
                    Assert.That(rect.Overlaps(fragments[j].atlasRegion), Is.False, "Overlapping rectangles " + i + "/" + j);
            }
        }

        [Test]
        public void FiveSkinTilesKeepBalancedDetailAndSharedRegions()
        {
            var material = Material(Enumerable.Repeat(new Vector2Int(2048, 2048), 5).ToArray());
            var owner = material.materialFragments[0];
            var shared = new UMAData.MaterialFragment { isRectShared = true, rectFragment = owner };
            material.materialFragments.Add(shared);
            Fit(material, UMAGeneratorBase.FitMethod.BestFitSquare);
            AssertValid(material);
            Assert.That(material.resolutionScale.x, Is.EqualTo(.25f));
            Assert.That(material.resolutionScale.y, Is.EqualTo(1f / 6).Within(.000001f));
            float occupied = material.materialFragments.Where(f => !f.isRectShared).Sum(f => f.atlasRegion.width * f.atlasRegion.height);
            Assert.That(occupied / (1024 * 1024), Is.InRange(.83f, .84f));
            Assert.That(shared.atlasRegion, Is.EqualTo(owner.atlasRegion));
        }

        [Test]
        public void SingleWideTextureDoesNotUpscaleItsShortAxis()
        {
            var material = Material(new Vector2Int(2048, 256));
            Fit(material, UMAGeneratorBase.FitMethod.BestFitSquare);
            Assert.That(material.materialFragments[0].atlasRegion, Is.EqualTo(new Rect(0, 0, 1024, 256)));
            Assert.That(material.resolutionScale, Is.EqualTo(new Vector2(.5f, 1)));
        }

        [Test]
        public void TwoDifferentRectanglesKeepDetailInBothAxes()
        {
            var material = Material(new Vector2Int(2048, 128), new Vector2Int(128, 2048));
            Fit(material, UMAGeneratorBase.FitMethod.BestFitSquare);
            AssertValid(material);
            Assert.That(Mathf.Min(material.resolutionScale.x, material.resolutionScale.y), Is.GreaterThan(.47f));
        }

        [Test]
        public void TilesThatAlreadyFitRetainTheirOriginalSize()
        {
            var material = Material(Enumerable.Repeat(new Vector2Int(256, 256), 5).ToArray());
            Fit(material, UMAGeneratorBase.FitMethod.BestFitSquare);
            AssertValid(material);
            Assert.That(material.resolutionScale, Is.EqualTo(Vector2.one));
            foreach (var fragment in material.materialFragments)
                Assert.That(fragment.atlasRegion.size, Is.EqualTo(new Vector2(256, 256)));
        }

        private static readonly Vector2Int[] HeuristicRegression = {
            new Vector2Int(640, 576), new Vector2Int(1792, 832), new Vector2Int(768, 1792),
            new Vector2Int(512, 1664), new Vector2Int(128, 1728), new Vector2Int(320, 1088),
            new Vector2Int(2048, 640), new Vector2Int(1728, 320), new Vector2Int(2048, 1984), new Vector2Int(1728, 512)
        };

        [Test]
        public void MultipleHeuristicsKeepsEveryRectangleWhenTheLastHeuristicFails()
        {
            var material = Material(HeuristicRegression);
            Fit(material, UMAGeneratorBase.FitMethod.MultipleHeuristics);
            AssertValid(material);
        }

        [Test]
        public void MultipleHeuristicsRestoresCoordinatesMatchingItsWinningBounds()
        {
            var material = Material(HeuristicRegression);
            Call("PrepareAtlasRectangles", material);
            object[] args = { Area(4544, 3072), null, null };
            Assert.That((bool)Call("TryPackBest", args), Is.True);
            AssertValid(material, 4544, 3072);
            var packType = args[1].GetType();
            Assert.That(material.materialFragments.Max(f => f.atlasRegion.xMax), Is.EqualTo((int)packType.GetField("xMax").GetValue(args[1])));
            Assert.That(material.materialFragments.Max(f => f.atlasRegion.yMax), Is.EqualTo((int)packType.GetField("yMax").GetValue(args[1])));
        }

        [TestCase(UMAGeneratorBase.FitMethod.BestFitSquare)]
        [TestCase(UMAGeneratorBase.FitMethod.MultipleHeuristics)]
        public void ScaledEdgesNeverExtendOnePixelBeyondTheAtlas(UMAGeneratorBase.FitMethod method)
        {
            var material = Material(new Vector2Int(1344, 256), new Vector2Int(1920, 1152),
                new Vector2Int(1792, 1792), new Vector2Int(448, 704), new Vector2Int(1408, 1408));
            Fit(material, method);
            AssertValid(material);
        }

        [TestCase(UMAGeneratorBase.FitMethod.BestFitSquare)]
        [TestCase(UMAGeneratorBase.FitMethod.MultipleHeuristics)]
        public void RepeatedFitsReadCurrentCropOverlayScaleAndTextureDimensions(UMAGeneratorBase.FitMethod method)
        {
            var material = Material(new Vector2Int(2048, 2048));
            var fragment = material.materialFragments[0];
            Fit(material, method);
            fragment.sourceUVRect = new Rect(0, 0, .5f, .5f);
            Fit(material, method);
            Assert.That(fragment.atlasRegion.size, Is.EqualTo(new Vector2(1024, 1024)));
            fragment.slotData.overlayScale = .5f;
            Fit(material, method);
            Assert.That(fragment.atlasRegion.size, Is.EqualTo(new Vector2(512, 512)));
            source.width = source.height = 1024;
            Fit(material, method, 2);
            Assert.That(fragment.atlasRegion.size, Is.EqualTo(new Vector2(128, 128)));
        }

        [TestCase(UMAGeneratorBase.FitMethod.BestFitSquare)]
        [TestCase(UMAGeneratorBase.FitMethod.MultipleHeuristics)]
        [TestCase(UMAGeneratorBase.FitMethod.DecreaseResolution)]
        public void FullFittingPassClearsMissingAndZeroSizedInputs(UMAGeneratorBase.FitMethod method)
        {
            var material = Material(new Vector2Int(2048, 2048), new Vector2Int(2048, 2048), new Vector2Int(2048, 2048));
            var empty = material.materialFragments[1];
            empty.baseOverlay.textureList = Array.Empty<Texture>();
            var zero = material.materialFragments[2];
            zero.slotData.overlayScale = 0;
            empty.atlasRegion = zero.atlasRegion = new Rect(50, 50, 100, 100);
            var shared = new UMAData.MaterialFragment { isRectShared = true, rectFragment = material.materialFragments[0] };
            material.materialFragments.Add(shared);
            generator.AtlasOverflowFitMethod = method;
            generator.FitPercentageDecrease = .5f;
            SetField("scaleFactor", 1);
            SetField("atlassedMaterials", new List<UMAData.GeneratedMaterial> { material });
            Call("GenerateAtlasData");
            Assert.That(empty.atlasRegion, Is.EqualTo(Rect.zero));
            Assert.That(zero.atlasRegion, Is.EqualTo(Rect.zero));
            Assert.That(shared.atlasRegion, Is.EqualTo(material.materialFragments[0].atlasRegion));
            Assert.That(shared.atlasRegion.size, Is.EqualTo(new Vector2(1024, 1024)));
        }

        [Test]
        public void DisablingFitDoesNotSilentlyRescaleAnOversizedRectangle()
        {
            generator.fitAtlas = false;
            var material = Material(new Vector2Int(2048, 2048));
            LogAssert.Expect(LogType.Log, "Atlas fitting is disabled and the atlas is too small. Textures that do not fit are skipped. Enable atlas fitting or increase the atlas resolution.");
            Fit(material, UMAGeneratorBase.FitMethod.BestFitSquare);
            Assert.That(material.resolutionScale, Is.EqualTo(Vector2.one));
            Assert.That(material.materialFragments[0].atlasRegion, Is.EqualTo(Rect.zero));
        }

        [TestCase(UMAGeneratorBase.FitMethod.BestFitSquare)]
        [TestCase(UMAGeneratorBase.FitMethod.MultipleHeuristics)]
        public void SeededMixedRectanglesAlwaysFitWithoutOverlapOrMissingRegions(UMAGeneratorBase.FitMethod method)
        {
            var random = new System.Random(42171);
            for (int trial = 0; trial < 1000; trial++)
            {
                var sizes = new Vector2Int[random.Next(3, 16)];
                for (int i = 0; i < sizes.Length; i++) sizes[i] = new Vector2Int(random.Next(2, 33) * 64, random.Next(2, 33) * 64);
                var material = Material(sizes);
                Fit(material, method);
                AssertValid(material);
            }
        }
    }
}
#endif
