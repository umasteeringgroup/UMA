#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class OverlayPainterSpriteSetTests
    {
        [Test]
        public void OptionalSpriteNamesFallBackToImportedName()
        {
            var spriteSet = ScriptableObject.CreateInstance<OverlayPainterSpriteSet>();
            try
            {
                spriteSet.setName = "Leather";
                spriteSet.spriteNames.Add("Light Brown Leather");

                Assert.That(spriteSet.DisplayName, Is.EqualTo("Leather"));
                Assert.That(spriteSet.GetSpriteName(0, "Sheet_0"),
                    Is.EqualTo("Light Brown Leather"));
                Assert.That(spriteSet.GetSpriteName(1, "Sheet_1"), Is.EqualTo("Sheet_1"));
            }
            finally
            {
                Object.DestroyImmediate(spriteSet);
            }
        }

        [TestCase("LeatherStampsAlbedoMap_0", 0)]
        [TestCase("LeatherStampsAlbedoMap_15", 15)]
        [TestCase("LeatherStampsAlbedoMap", -1)]
        public void ImportedSpriteSuffixDeterminesCrossSheetIndex(string spriteName, int expected)
        {
            Assert.That(OverlayPainterSpriteSetEditorUtility.ParseTrailingIndex(spriteName),
                Is.EqualTo(expected));
        }

        [Test]
        public void LeatherSampleHasThreeAlignedSixteenSpriteSheets()
        {
            OverlayPainterSpriteSet spriteSet = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Textures/LeatherSpriteSet.asset"));

            Assert.That(spriteSet, Is.Not.Null);
            Assert.That(spriteSet.spriteSheets, Has.Count.EqualTo(3));
            Assert.That(spriteSet.spriteNames, Has.Count.EqualTo(16));
            Assert.That(OverlayPainterSpriteSetEditorUtility.GetCommonSpriteCount(spriteSet),
                Is.EqualTo(16));
            Assert.That(spriteSet.spriteSheets[0].channel, Is.EqualTo(TexturePaintChannel.Albedo));
            Assert.That(spriteSet.spriteSheets[1].channel, Is.EqualTo(TexturePaintChannel.Roughness));
            Assert.That(spriteSet.spriteSheets[1].inverted, Is.True);
            Assert.That(spriteSet.spriteSheets[2].channel, Is.EqualTo(TexturePaintChannel.Normal));
        }

        [Test]
        public void SpriteSetFillSettingsKeepProjectionTilingAndSpriteSource()
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f));
            try
            {
                TexturePaintFillSettings settings =
                    TexturePaintStageWindow.CreateSpriteSetFillSettings(sprite, true,
                        new Vector2(3f, 7f), TexturePaintFillProjection.Triplanar,
                        TexturePaintNormalConvention.DirectX);

                Assert.That(settings.source, Is.EqualTo(TexturePaintBrushSource.Texture));
                Assert.That(settings.sourceSprite, Is.SameAs(sprite));
                Assert.That(settings.sourceTexture, Is.Null);
                Assert.That(settings.invert, Is.True);
                Assert.That(settings.projection, Is.EqualTo(TexturePaintFillProjection.Triplanar));
                Assert.That(settings.tiling, Is.EqualTo(new Vector2(3f, 7f)));
                Assert.That(settings.normalConvention,
                    Is.EqualTo(TexturePaintNormalConvention.DirectX));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SpriteSetFillTilingIsClampedToSupportedRange()
        {
            TexturePaintFillSettings settings =
                TexturePaintStageWindow.CreateSpriteSetFillSettings(null, false,
                    new Vector2(0f, 2000f), TexturePaintFillProjection.Flat,
                    TexturePaintNormalConvention.OpenGL);

            Assert.That(settings.tiling, Is.EqualTo(new Vector2(0.01f, 1000f)));
        }

        [Test]
        public void SpriteSetPathSettingsKeepSpriteChannelAndNormalConvention()
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f));
            try
            {
                TexturePaintChannelSourceSettings settings =
                    TexturePaintStageWindow.CreateSpriteSetPathSourceSettings(sprite, true,
                        TexturePaintNormalConvention.DirectX);

                Assert.That(settings.source, Is.EqualTo(TexturePaintBrushSource.Texture));
                Assert.That(settings.sourceSprite, Is.SameAs(sprite));
                Assert.That(settings.sourceTexture, Is.Null);
                Assert.That(settings.invert, Is.True);
                Assert.That(settings.normalConvention,
                    Is.EqualTo(TexturePaintNormalConvention.DirectX));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void OverlayPathSettingsKeepLiveOverlaySource()
        {
            OverlayDataAsset overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                TexturePaintChannelSourceSettings settings =
                    TexturePaintStageWindow.CreateOverlayPathSourceSettings(overlay,
                        TexturePaintNormalConvention.OpenGL);

                Assert.That(settings.source, Is.EqualTo(TexturePaintBrushSource.Overlay));
                Assert.That(settings.sourceOverlay, Is.SameAs(overlay));
                Assert.That(settings.color, Is.EqualTo(Color.white));
                Assert.That(settings.normalConvention,
                    Is.EqualTo(TexturePaintNormalConvention.OpenGL));
            }
            finally
            {
                Object.DestroyImmediate(overlay);
            }
        }

        [Test]
        public void PathSourcePopulationIgnoresStaleUnauthoredAlbedoSettings()
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f));
            var layer = new TexturePaintLayer { kind = TexturePaintLayerKind.Spline };
            try
            {
                // The null target is sufficient here: source population should use the authored
                // channel keys and must not touch raster data.
                layer.channels[TexturePaintChannel.NormalControl] = null;
                var source = new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Texture,
                    sourceSprite = sprite,
                    color = Color.white
                };
                layer.GetChannelSettings(TexturePaintChannel.NormalControl).sourceSettings =
                    source.Clone();
                layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings = source.Clone();
                var context = new StrokeContext
                {
                    channel = TexturePaintChannel.Albedo,
                    paintSource = TexturePaintBrushSource.Texture,
                    sourceSprite = sprite,
                    color = Color.white
                };

                TexturePaintStageWindow.PopulateLayerChannelSources(context, layer);

                Assert.That(context.channelSources.Keys,
                    Is.EquivalentTo(new[] { TexturePaintChannel.NormalControl }));
                Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Albedo), Is.False);
            }
            finally
            {
                layer.channels.Clear();
                layer.Dispose();
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
#endif
