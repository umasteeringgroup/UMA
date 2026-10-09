#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class SpriteGridOptionsTests
    {
        [Test]
        public void GridRectsUseInsetAndTopLeftNamingOrder()
        {
            RectInt[] rects = SpriteGridProcessor.BuildSpriteRects(8, 8, 2, 2, 1);

            Assert.That(rects, Has.Length.EqualTo(4));
            Assert.That(rects[0], Is.EqualTo(new RectInt(1, 5, 2, 2)));
            Assert.That(rects[1], Is.EqualTo(new RectInt(5, 5, 2, 2)));
            Assert.That(rects[2], Is.EqualTo(new RectInt(1, 1, 2, 2)));
            Assert.That(rects[3], Is.EqualTo(new RectInt(5, 1, 2, 2)));
        }

        [Test]
        public void LeatherGridInsetRemovesTwoPixelsFromEveryCellEdge()
        {
            RectInt[] rects = SpriteGridProcessor.BuildSpriteRects(1360, 1360, 4, 4, 2);

            Assert.That(rects, Has.Length.EqualTo(16));
            Assert.That(rects[0], Is.EqualTo(new RectInt(2, 1022, 336, 336)));
            Assert.That(rects[5], Is.EqualTo(new RectInt(342, 682, 336, 336)));
            Assert.That(rects[15], Is.EqualTo(new RectInt(1022, 2, 336, 336)));
        }

        [Test]
        public void IndividualSpriteInsetsAndOffsetsAdjustEveryRectangleEdge()
        {
            var settings = new[]
            {
                new SpriteGridOptions(2, 1, 1, false, false,
                    horizontalOffset: 1, verticalOffset: 0,
                    insetX1: 1, insetY1: 1, insetX2: 2, insetY2: 1),
                new SpriteGridOptions(2, 1, 1, false, false,
                    horizontalOffset: -1, verticalOffset: 0,
                    insetX1: 2, insetY1: 0, insetX2: 1, insetY2: 2)
            };

            RectInt[] rects = SpriteGridProcessor.BuildSpriteRects(20, 10, 2, 1, 1, settings);

            Assert.That(rects[0], Is.EqualTo(new RectInt(2, 1, 7, 8)));
            Assert.That(rects[1], Is.EqualTo(new RectInt(11, 0, 7, 8)));
        }

        [Test]
        public void IndividualSpriteOffsetCannotMoveRectangleOutsideTexture()
        {
            var settings = new[]
            {
                new SpriteGridOptions(1, 1, 1, false, false,
                    horizontalOffset: -2, insetX1: 1, insetY1: 1, insetX2: 1, insetY2: 1)
            };

            Assert.Throws<System.ArgumentException>(() =>
                SpriteGridProcessor.BuildSpriteRects(10, 10, 1, 1, 1, settings));
        }

        [Test]
        public void IndividualSpriteSettingsRoundTripSpatialAndProcessingValues()
        {
            var original = new SpriteGridOptions(3, 2, 1, true, true,
                applyClahe: true, claheStrength: 0.65f,
                applyMicroWarping: true, warpStrength: 0.35f,
                horizontalOffset: -3, verticalOffset: 4,
                insetX1: 2, insetY1: 3, insetX2: 4, insetY2: 5,
                seamBlendFraction: 0.12f);

            SpriteGridOptions restored = new SpriteGridSpriteSettings(original).ToOptions(3, 2, 1);

            Assert.That(restored.insetX1, Is.EqualTo(2));
            Assert.That(restored.insetY1, Is.EqualTo(3));
            Assert.That(restored.insetX2, Is.EqualTo(4));
            Assert.That(restored.insetY2, Is.EqualTo(5));
            Assert.That(restored.horizontalOffset, Is.EqualTo(-3));
            Assert.That(restored.verticalOffset, Is.EqualTo(4));
            Assert.That(restored.makeTileable, Is.True);
            Assert.That(restored.seamBlendFraction, Is.EqualTo(0.12f));
            Assert.That(restored.fixBrightnessGradient, Is.True);
            Assert.That(restored.applyClahe, Is.True);
            Assert.That(restored.claheStrength, Is.EqualTo(0.65f));
            Assert.That(restored.applyMicroWarping, Is.True);
            Assert.That(restored.warpStrength, Is.EqualTo(0.35f));
        }

        [Test]
        public void IndividualProcessingProfileOnlyChangesItsOwnSprite()
        {
            const int width = 8;
            const int height = 4;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int tileX = x % 4;
                byte value = (byte)(20 + tileX * 60);
                pixels[y * width + x] = new Color32(value, value, value, 255);
            }
            var rects = new[]
            {
                new RectInt(0, 0, 4, height),
                new RectInt(4, 0, 4, height)
            };
            var settings = new[]
            {
                new SpriteGridOptions(2, 1, 0, true, false),
                new SpriteGridOptions(2, 1, 0, false, false)
            };

            SpriteGridProcessor.ApplyTileFixes(pixels, width, rects, settings);

            Assert.That(pixels[2 * width], Is.EqualTo(pixels[2 * width + 3]));
            Assert.That(pixels[2 * width + 4], Is.Not.EqualTo(pixels[2 * width + 7]));
        }

        [Test]
        public void LegacySpriteAreasCanBeCopiedIntoEditableProfiles()
        {
            var sourceRects = new[]
            {
                new RectInt(2, 1, 7, 8),
                new RectInt(11, 0, 7, 8)
            };

            bool copied = SpriteGridProcessor.TryBuildConfigurationFromRects(
                20, 10, sourceRects, 2, 1, out SpriteGridSavedConfiguration configuration);

            Assert.That(copied, Is.True);
            var options = new SpriteGridOptions[configuration.spriteSettings.Count];
            for (int i = 0; i < options.Length; i++)
                options[i] = configuration.spriteSettings[i].ToOptions(
                    configuration.columns, configuration.rows, configuration.inset);
            RectInt[] rebuilt = SpriteGridProcessor.BuildSpriteRects(20, 10,
                configuration.columns, configuration.rows, configuration.inset, options);

            Assert.That(configuration.editSpritesIndividually, Is.True);
            Assert.That(rebuilt, Is.EqualTo(sourceRects));
        }

        [Test]
        public void LegacySpriteCopyInfersGridWhenCurrentGridHasSameCountButWrongShape()
        {
            RectInt[] sourceRects = SpriteGridProcessor.BuildSpriteRects(80, 20, 8, 2, 1);

            bool copied = SpriteGridProcessor.TryBuildConfigurationFromRects(
                80, 20, sourceRects, 4, 4, out SpriteGridSavedConfiguration configuration);

            Assert.That(copied, Is.True);
            Assert.That(configuration.columns, Is.EqualTo(8));
            Assert.That(configuration.rows, Is.EqualTo(2));
        }

        [Test]
        public void SavedSpriteConfigurationCloneRetainsSettingsWithoutAliasing()
        {
            var source = new SpriteGridSavedConfiguration
            {
                sourceWidth = 64,
                sourceHeight = 32,
                columns = 2,
                rows = 1,
                inset = 1,
                editSpritesIndividually = true,
                globalSettings = new SpriteGridSpriteSettings(new SpriteGridOptions(
                    2, 1, 1, true, false, horizontalOffset: 3,
                    insetX1: 2, insetY1: 3, insetX2: 4, insetY2: 5))
            };
            source.spriteSettings.Add(new SpriteGridSpriteSettings(new SpriteGridOptions(
                2, 1, 1, false, true, verticalOffset: -2,
                insetX1: 1, insetY1: 2, insetX2: 3, insetY2: 4)));
            source.spriteSettings.Add(new SpriteGridSpriteSettings(new SpriteGridOptions(
                2, 1, 1, true, false, horizontalOffset: -4,
                insetX1: 4, insetY1: 3, insetX2: 2, insetY2: 1)));

            SpriteGridSavedConfiguration clone = source.Clone();
            clone.spriteSettings[0].insetX1 = 99;

            Assert.That(clone.sourceWidth, Is.EqualTo(64));
            Assert.That(clone.columns, Is.EqualTo(2));
            Assert.That(clone.editSpritesIndividually, Is.True);
            Assert.That(clone.globalSettings.horizontalOffset, Is.EqualTo(3));
            Assert.That(clone.spriteSettings[0].verticalOffset, Is.EqualTo(-2));
            Assert.That(clone.spriteSettings[1].horizontalOffset, Is.EqualTo(-4));
            Assert.That(source.spriteSettings[0].insetX1, Is.EqualTo(1));
        }

        [Test]
        public void SavedSpriteConfigurationSurvivesJsonRoundTrip()
        {
            var source = new SpriteGridSavedConfiguration
            {
                sourceWidth = 128,
                sourceHeight = 64,
                columns = 1,
                rows = 1,
                inset = 2,
                editSpritesIndividually = true,
                globalSettings = new SpriteGridSpriteSettings(new SpriteGridOptions(
                    1, 1, 2, true, true, applyBlueNoiseDithering: true,
                    blueNoiseStrength: 0.45f, horizontalOffset: 5, verticalOffset: -3,
                    insetX1: 1, insetY1: 2, insetX2: 3, insetY2: 4,
                    seamBlendFraction: 0.08f))
            };
            source.spriteSettings.Add(new SpriteGridSpriteSettings(
                source.globalSettings.ToOptions(1, 1, 2)));

            string json = JsonUtility.ToJson(source);
            SpriteGridSavedConfiguration restored =
                JsonUtility.FromJson<SpriteGridSavedConfiguration>(json);

            Assert.That(SpriteGridConfigurationStore.IsValid(restored), Is.True);
            Assert.That(restored.sourceWidth, Is.EqualTo(128));
            Assert.That(restored.spriteSettings[0].insetX2, Is.EqualTo(3));
            Assert.That(restored.spriteSettings[0].horizontalOffset, Is.EqualTo(5));
            Assert.That(restored.spriteSettings[0].verticalOffset, Is.EqualTo(-3));
            Assert.That(restored.spriteSettings[0].applyBlueNoiseDithering, Is.True);
            Assert.That(restored.spriteSettings[0].blueNoiseStrength, Is.EqualTo(0.45f));
            Assert.That(restored.spriteSettings[0].seamBlendFraction, Is.EqualTo(0.08f));
        }

        [Test]
        public void PreviewBestFitUsesAvailableHeightInAWideColumn()
        {
            Rect fitted = SpriteGridOptionsWindow.BestFitRect(new Rect(0f, 0f, 1000f, 500f), 1f);

            Assert.That(fitted.width, Is.EqualTo(484f).Within(0.01f));
            Assert.That(fitted.height, Is.EqualTo(484f).Within(0.01f));
            Assert.That(fitted.center, Is.EqualTo(new Vector2(500f, 250f)));
        }

        [Test]
        public void PreviewBestFitUsesAvailableWidthInATallColumn()
        {
            Rect fitted = SpriteGridOptionsWindow.BestFitRect(new Rect(0f, 0f, 500f, 1000f), 2f);

            Assert.That(fitted.width, Is.EqualTo(484f).Within(0.01f));
            Assert.That(fitted.height, Is.EqualTo(242f).Within(0.01f));
            Assert.That(fitted.center, Is.EqualTo(new Vector2(250f, 500f)));
        }

        [Test]
        public void SeamlessFixMatchesOppositeEdgesWithoutTouchingInsetPixels()
        {
            const int width = 8;
            var pixels = new Color32[width * width];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(3, 7, 11, 13);
            RectInt tile = new RectInt(1, 1, 6, 6);
            for (int y = tile.yMin; y < tile.yMax; y++)
            for (int x = tile.xMin; x < tile.xMax; x++)
                pixels[y * width + x] = new Color32((byte)(x * 25), (byte)(y * 25), 90, 255);

            SpriteGridProcessor.MakeSeamlesslyTileable(pixels, width, tile);

            for (int y = tile.yMin; y < tile.yMax; y++)
                Assert.That(pixels[y * width + tile.xMin],
                    Is.EqualTo(pixels[y * width + tile.xMax - 1]));
            for (int x = tile.xMin; x < tile.xMax; x++)
                Assert.That(pixels[tile.yMin * width + x],
                    Is.EqualTo(pixels[(tile.yMax - 1) * width + x]));
            Assert.That(pixels[0], Is.EqualTo(new Color32(3, 7, 11, 13)));
        }

        [Test]
        public void SeamBlendAreaLimitsHowFarCorrectionReachesFromEdges()
        {
            const int width = 20;
            const int height = 8;
            var original = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte value = (byte)(10 + x * 10);
                original[y * width + x] = new Color32(value, value, value, 255);
            }
            var narrow = (Color32[])original.Clone();
            var wide = (Color32[])original.Clone();
            var rects = new[] { new RectInt(0, 0, width, height) };

            SpriteGridProcessor.ApplyTileFixes(narrow, width, rects,
                new SpriteGridOptions(1, 1, 0, true, false, seamBlendFraction: 0.1f));
            SpriteGridProcessor.ApplyTileFixes(wide, width, rects,
                new SpriteGridOptions(1, 1, 0, true, false, seamBlendFraction: 0.25f));

            int row = height / 2 * width;
            Assert.That(narrow[row], Is.Not.EqualTo(original[row]));
            Assert.That(narrow[row + 2], Is.EqualTo(original[row + 2]));
            Assert.That(wide[row + 2], Is.Not.EqualTo(original[row + 2]));
            Assert.That(SpriteGridProcessor.CalculateSeamBlendPixels(width, 0.1f), Is.EqualTo(2));
            Assert.That(SpriteGridProcessor.CalculateSeamBlendPixels(width, 0.25f), Is.EqualTo(5));
        }

        [Test]
        public void BrightnessFixRemovesPlanarGradientAndPreservesAlpha()
        {
            const int width = 8;
            var pixels = new Color32[width * width];
            for (int y = 0; y < width; y++)
            for (int x = 0; x < width; x++)
            {
                byte value = (byte)(40 + x * 20 + y * 5);
                pixels[y * width + x] = new Color32(value, value, value, 173);
            }

            SpriteGridProcessor.RemoveBrightnessGradient(pixels, width, new RectInt(0, 0, width, width));

            int left = 0;
            int right = 0;
            for (int y = 0; y < width; y++)
            {
                left += pixels[y * width].r;
                right += pixels[y * width + width - 1].r;
                Assert.That(pixels[y * width].a, Is.EqualTo(173));
            }
            Assert.That(Mathf.Abs(right - left), Is.LessThanOrEqualTo(8));
        }

        [Test]
        public void InsetCannotConsumeAnEntireCell()
        {
            Assert.Throws<System.ArgumentException>(() =>
                SpriteGridProcessor.BuildSpriteRects(8, 8, 4, 4, 1));
        }

        [Test]
        public void PolynomialGradientRemovalFlattensQuadraticLuminance()
        {
            const int size = 24;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x * 2f / (size - 1f) - 1f;
                float ny = y * 2f / (size - 1f) - 1f;
                float value = 0.3f + 0.12f * nx * nx + 0.09f * ny * ny +
                    0.06f * nx * ny + 0.05f * nx - 0.03f * ny;
                pixels[y * size + x] = Gray(value, 181);
            }

            float rangeBefore = LuminanceRange(pixels);
            Process(pixels, size, new SpriteGridOptions(1, 1, 0, false, false,
                removePolynomialGradient: true));

            Assert.That(LuminanceRange(pixels), Is.LessThan(rangeBefore * 0.08f));
            Assert.That(pixels[size / 2].a, Is.EqualTo(181));
        }

        [Test]
        public void ClaheExpandsLocalContrastAndBlendsRegions()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float detail = ((x % 8) + (y % 8)) / 14f;
                pixels[y * size + x] = Gray(0.43f + detail * 0.14f);
            }

            float rangeBefore = LuminanceRange(pixels);
            Process(pixels, size, new SpriteGridOptions(1, 1, 0, false, false,
                applyClahe: true, claheStrength: 1f));

            Assert.That(LuminanceRange(pixels), Is.GreaterThan(rangeBefore * 1.5f));
            Assert.That(MaxAdjacentDifference(pixels, size), Is.LessThan(0.8f));
        }

        [Test]
        public void BilateralFilterReducesNoiseWhilePreservingAnEdge()
        {
            const int width = 32;
            const int height = 16;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float baseValue = x < width / 2 ? 0.25f : 0.75f;
                float noise = ((x + y) & 1) == 0 ? -0.06f : 0.06f;
                pixels[y * width + x] = Gray(baseValue + noise);
            }

            float varianceBefore = InteriorVariance(pixels, width, height);
            Process(pixels, width, new SpriteGridOptions(1, 1, 0, false, false,
                applyBilateralFilter: true, bilateralStrength: 1f));

            Assert.That(InteriorVariance(pixels, width, height), Is.LessThan(varianceBefore * 0.35f));
            float leftMean = RegionMean(pixels, width, 2, 0, width / 2 - 2, height);
            float rightMean = RegionMean(pixels, width, width / 2 + 2, 0, width - 2, height);
            Assert.That(rightMean - leftMean, Is.GreaterThan(0.4f));
        }

        [Test]
        public void LinearSpaceConversionMatchesSrgbTransferFunctionAndRoundTrips()
        {
            float linear = TileImageProcessor.SrgbToLinear(0.5f);

            Assert.That(linear, Is.EqualTo(0.214041f).Within(0.00001f));
            Assert.That(TileImageProcessor.LinearToSrgb(linear), Is.EqualTo(0.5f).Within(0.00001f));
        }

        [Test]
        public void TileNormalizationMovesEveryTileToTheGlobalMean()
        {
            const int width = 16;
            const int height = 8;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = Gray(x < width / 2 ? 0.2f : 0.8f);
            var rects = new[]
            {
                new RectInt(0, 0, width / 2, height),
                new RectInt(width / 2, 0, width / 2, height)
            };

            SpriteGridProcessor.ApplyTileFixes(pixels, width, rects,
                new SpriteGridOptions(2, 1, 0, false, false, normalizationStrength: 1f));

            float left = RegionMean(pixels, width, 0, 0, width / 2, height);
            float right = RegionMean(pixels, width, width / 2, 0, width, height);
            Assert.That(left, Is.EqualTo(right).Within(1f / 255f));
            Assert.That(left, Is.EqualTo(0.5f).Within(1f / 255f));
        }

        [Test]
        public void PoissonBlendMatchesOppositeEdgesAndRetainsInteriorDetail()
        {
            const int size = 24;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1);
                float detail = ((x / 3 + y / 3) & 1) == 0 ? 0.08f : -0.08f;
                pixels[y * size + x] = Gray(0.2f + nx * 0.55f + detail);
            }

            Process(pixels, size, new SpriteGridOptions(1, 1, 0, false, false,
                poissonSeamlessBlend: true));

            for (int y = 0; y < size; y++)
                Assert.That(pixels[y * size].r, Is.EqualTo(pixels[y * size + size - 1].r));
            for (int x = 0; x < size; x++)
                Assert.That(pixels[x].r, Is.EqualTo(pixels[(size - 1) * size + x].r));
            Assert.That(LuminanceRange(pixels), Is.GreaterThan(0.1f));
        }

        [Test]
        public void BlueNoiseDitheringAddsSubtlePeriodicLuminanceVariation()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Gray(0.5f, 177);

            Process(pixels, size, new SpriteGridOptions(1, 1, 0, false, false,
                applyBlueNoiseDithering: true, blueNoiseStrength: 1f));

            Assert.That(LuminanceRange(pixels), Is.GreaterThan(0.005f));
            Assert.That(LuminanceRange(pixels), Is.LessThanOrEqualTo(0.04f));
            AssertPeriodicEdges(pixels, size, size);
            Assert.That(pixels[size / 2].a, Is.EqualTo(177));
        }

        [Test]
        public void MultiOctaveNoiseUsesDifferentPeriodicSeedsPerTile()
        {
            const int tileSize = 32;
            const int width = tileSize * 2;
            var pixels = new Color32[width * tileSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Gray(0.5f);
            var rects = new[]
            {
                new RectInt(0, 0, tileSize, tileSize),
                new RectInt(tileSize, 0, tileSize, tileSize)
            };

            SpriteGridProcessor.ApplyTileFixes(pixels, width, rects,
                new SpriteGridOptions(2, 1, 0, false, false,
                    applyMultiOctaveNoise: true, noiseStrength: 1f, noiseFrequency: 0.5f));

            int differentSamples = 0;
            for (int y = 0; y < tileSize; y++)
            for (int x = 0; x < tileSize; x++)
                if (pixels[y * width + x].r != pixels[y * width + tileSize + x].r)
                    differentSamples++;
            Assert.That(differentSamples, Is.GreaterThan(tileSize * tileSize / 3));
            Assert.That(TileRange(pixels, width, rects[0]), Is.GreaterThan(0.01f));
            Assert.That(TileRange(pixels, width, rects[1]), Is.GreaterThan(0.01f));
            AssertPeriodicEdges(pixels, width, rects[0]);
            AssertPeriodicEdges(pixels, width, rects[1]);
        }

        [Test]
        public void MicroWarpChangesStructureWithoutOpeningTileSeams()
        {
            const int size = 48;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float angleX = x * Mathf.PI * 2f / (size - 1f);
                float angleY = y * Mathf.PI * 2f / (size - 1f);
                pixels[y * size + x] = Gray(0.5f + 0.22f * Mathf.Sin(angleX * 5f) *
                    Mathf.Cos(angleY * 3f), 203);
            }
            Color32[] original = (Color32[])pixels.Clone();

            Process(pixels, size, new SpriteGridOptions(1, 1, 0, false, false,
                applyMicroWarping: true, warpStrength: 1f, warpFrequency: 0.5f));

            Assert.That(MeanAbsoluteDifference(original, pixels), Is.GreaterThan(0.003f));
            Assert.That(Mathf.Abs(Average(pixels) - Average(original)), Is.LessThan(0.015f));
            AssertPeriodicEdges(pixels, size, size);
            Assert.That(pixels[size + 1].a, Is.EqualTo(203));
        }

        [Test]
        public void FrequencyScramblingChangesLowFrequenciesButPreservesMean()
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float value = 0.5f + 0.18f * Mathf.Cos((2f * (x % 8) + 1f) * Mathf.PI / 16f) +
                    0.12f * Mathf.Cos((2f * (y % 8) + 1f) * Mathf.PI / 16f);
                pixels[y * size + x] = Gray(value, 219);
            }
            Color32[] original = (Color32[])pixels.Clone();

            Process(pixels, size, new SpriteGridOptions(1, 1, 0, false, false,
                applyFrequencyScrambling: true, scrambleStrength: 1f));

            float difference = MeanAbsoluteDifference(original, pixels);
            Assert.That(difference, Is.GreaterThan(0.001f));
            Assert.That(difference, Is.LessThan(0.04f));
            Assert.That(Average(pixels), Is.EqualTo(Average(original)).Within(1f / 255f));
            Assert.That(pixels[size + 1].a, Is.EqualTo(219));
        }

        private static void Process(Color32[] pixels, int width, SpriteGridOptions options)
        {
            int height = pixels.Length / width;
            SpriteGridProcessor.ApplyTileFixes(pixels, width,
                new[] { new RectInt(0, 0, width, height) }, options);
        }

        private static Color32 Gray(float value, byte alpha = 255)
        {
            byte channel = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
            return new Color32(channel, channel, channel, alpha);
        }

        private static float LuminanceRange(Color32[] pixels)
        {
            byte minimum = 255;
            byte maximum = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                minimum = System.Math.Min(minimum, pixels[i].r);
                maximum = System.Math.Max(maximum, pixels[i].r);
            }
            return (maximum - minimum) / 255f;
        }

        private static float TileRange(Color32[] pixels, int width, RectInt rect)
        {
            byte minimum = 255;
            byte maximum = 0;
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                byte value = pixels[y * width + x].r;
                minimum = System.Math.Min(minimum, value);
                maximum = System.Math.Max(maximum, value);
            }
            return (maximum - minimum) / 255f;
        }

        private static float Average(Color32[] pixels)
        {
            float sum = 0f;
            for (int i = 0; i < pixels.Length; i++) sum += pixels[i].r / 255f;
            return sum / pixels.Length;
        }

        private static float MeanAbsoluteDifference(Color32[] first, Color32[] second)
        {
            float sum = 0f;
            for (int i = 0; i < first.Length; i++)
                sum += System.Math.Abs(first[i].r - second[i].r) / 255f;
            return sum / first.Length;
        }

        private static void AssertPeriodicEdges(Color32[] pixels, int width, int height)
            => AssertPeriodicEdges(pixels, width, new RectInt(0, 0, width, height));

        private static void AssertPeriodicEdges(Color32[] pixels, int width, RectInt rect)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
                Assert.That(pixels[y * width + rect.xMin].r,
                    Is.EqualTo(pixels[y * width + rect.xMax - 1].r));
            for (int x = rect.xMin; x < rect.xMax; x++)
                Assert.That(pixels[rect.yMin * width + x].r,
                    Is.EqualTo(pixels[(rect.yMax - 1) * width + x].r));
        }

        private static float MaxAdjacentDifference(Color32[] pixels, int width)
        {
            int height = pixels.Length / width;
            int maximum = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (x + 1 < width)
                    maximum = System.Math.Max(maximum, System.Math.Abs(pixels[index].r - pixels[index + 1].r));
                if (y + 1 < height)
                    maximum = System.Math.Max(maximum, System.Math.Abs(pixels[index].r - pixels[index + width].r));
            }
            return maximum / 255f;
        }

        private static float InteriorVariance(Color32[] pixels, int width, int height)
        {
            float left = RegionVariance(pixels, width, 2, 2, width / 2 - 2, height - 2);
            float right = RegionVariance(pixels, width, width / 2 + 2, 2, width - 2, height - 2);
            return (left + right) * 0.5f;
        }

        private static float RegionVariance(Color32[] pixels, int width,
            int xMin, int yMin, int xMax, int yMax)
        {
            float mean = RegionMean(pixels, width, xMin, yMin, xMax, yMax);
            float sum = 0f;
            int count = 0;
            for (int y = yMin; y < yMax; y++)
            for (int x = xMin; x < xMax; x++)
            {
                float difference = pixels[y * width + x].r / 255f - mean;
                sum += difference * difference;
                count++;
            }
            return sum / count;
        }

        private static float RegionMean(Color32[] pixels, int width,
            int xMin, int yMin, int xMax, int yMax)
        {
            float sum = 0f;
            int count = 0;
            for (int y = yMin; y < yMax; y++)
            for (int x = xMin; x < xMax; x++)
            {
                sum += pixels[y * width + x].r / 255f;
                count++;
            }
            return sum / count;
        }
    }
}
#endif
