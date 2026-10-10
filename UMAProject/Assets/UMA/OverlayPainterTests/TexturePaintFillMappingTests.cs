#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintFillMappingTests
    {
        [TestCase(TexturePaintFillProjection.Flat, TexturePaintTriplanarBlend.Hard)]
        [TestCase(TexturePaintFillProjection.Flat, TexturePaintTriplanarBlend.CrossFade)]
        [TestCase(TexturePaintFillProjection.Triplanar, TexturePaintTriplanarBlend.Hard)]
        [TestCase(TexturePaintFillProjection.Triplanar, TexturePaintTriplanarBlend.CrossFade)]
        public void LinkedSpriteMapsRenderWithTheCompleteMasterMapping(
            TexturePaintFillProjection projection, TexturePaintTriplanarBlend blend)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            using var generator = new TexturePaintFillGenerator(null);
            using var expected = new EditableTextureTarget("Expected fill mapping", 64, 64,
                RenderTextureFormat.ARGBHalf, null, Color.clear);
            fixture.set.fillGenerator = generator;
            // UVs and world coordinates must differ to expose Flat/Triplanar drift.
            fixture.owner.transform.localScale = new Vector3(2.7f, 1.3f, .8f);
            fixture.owner.transform.rotation = Quaternion.Euler(29, 41, 17);
            fixture.owner.transform.position = new Vector3(.23f, -.31f, .17f);
            var owned = new List<Object>();
            try
            {
                TexturePaintLayer layer = null;
                foreach (var channel in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Normal,
                    TexturePaintChannel.Roughness })
                {
                    if (channel != TexturePaintChannel.Albedo)
                        fixture.set.channels[channel] = new TextureChannelTarget
                        {
                            channel = channel, format = RenderTextureFormat.ARGBHalf,
                            editable = new EditableTextureTarget("Mapping " + channel, 64, 64,
                                RenderTextureFormat.ARGBHalf, null, Color.clear)
                        };
                    var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
                    owned.Add(texture);
                    var pixels = new Color[256];
                    for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                        pixels[y * 16 + x] = new Color(x < 7 ? .25f : .75f,
                            y < 5 ? .3f : .65f, .9f, 1);
                    texture.SetPixels(pixels); texture.Apply();
                    var sprite = Sprite.Create(texture, new Rect(2, 1, 12, 14), Vector2.one * .5f);
                    owned.Add(sprite);
                    var settings = TexturePaintStageWindow.CreateSpriteSetFillSettings(sprite,
                        channel == TexturePaintChannel.Roughness, Vector2.one,
                        TexturePaintFillProjection.Flat, channel == TexturePaintChannel.Normal
                            ? TexturePaintNormalConvention.DirectX : TexturePaintNormalConvention.OpenGL);
                    settings.useFirstChannelTransform = true;
                    if (layer == null) layer = fixture.set.AddFillLayer("Linked sprites", channel, settings);
                    else Assert.That(fixture.set.UpdateFillLayer(layer, channel, settings), Is.True);
                }

                var master = layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings;
                // Simulate a saved fill from before mapping synchronization and then retile it.
                foreach (float scale in new[] { 1.25f, 3.95f })
                {
                    master.projection = projection;
                    master.triplanarBlend = blend;
                    master.blendOffset = .21f;
                    master.blendSharpness = 11f;
                    master.tiling = new Vector2(scale, 2);
                    master.offset = new Vector2(.13f, -.19f);
                    master.rotation = 27;
                    foreach (var channel in new[] { TexturePaintChannel.Normal, TexturePaintChannel.Roughness })
                    {
                        var source = layer.GetChannelSettings(channel).sourceSettings;
                        source.projection = projection == TexturePaintFillProjection.Flat
                            ? TexturePaintFillProjection.Triplanar : TexturePaintFillProjection.Flat;
                        source.triplanarBlend = blend == TexturePaintTriplanarBlend.Hard
                            ? TexturePaintTriplanarBlend.CrossFade : TexturePaintTriplanarBlend.Hard;
                        source.blendOffset = 0; source.blendSharpness = 1;
                    }
                    Assert.That(fixture.set.RegenerateFillLayer(layer), Is.True);
                    foreach (var channel in new[] { TexturePaintChannel.Normal, TexturePaintChannel.Roughness })
                    {
                        var source = layer.GetChannelSettings(channel).sourceSettings;
                        Assert.That(source.invert, Is.EqualTo(channel == TexturePaintChannel.Roughness));
                        Assert.That(source.normalConvention, Is.EqualTo(channel == TexturePaintChannel.Normal
                            ? TexturePaintNormalConvention.DirectX : TexturePaintNormalConvention.OpenGL));
                        var reference = new TexturePaintFillSettings
                        {
                            source = TexturePaintBrushSource.Texture, projection = projection,
                            triplanarBlend = blend, blendOffset = .21f, blendSharpness = 11,
                            tiling = master.tiling, offset = master.offset, rotation = master.rotation
                        };
                        var resolved = TexturePaintSpriteSource.Resolve(null, source.sourceSprite,
                            channel, source.normalConvention, source.invert);
                        Assert.That(generator.Render(fixture.set, layer, expected, resolved, reference,
                            channel: channel), Is.True);
                        TexturePaintGpuTestFixture.AssertImage("Linked " + channel + " " + projection,
                            TexturePaintGpuTestFixture.ReadPixels(expected.Front),
                            TexturePaintGpuTestFixture.ReadPixels(layer.channels[channel].Front));
                    }
                }
                Assert.That(layer.fillSettings.generatorRevision, Is.EqualTo(TexturePaintFillGenerator.CurrentRevision));
            }
            finally
            {
                for (int i = owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(owned[i]);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MappingSelectionAndSynchronizationRespectLinkToggle(bool linked)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var layer = fixture.set.AddFillLayer("Mapping", TexturePaintChannel.Albedo,
                new TexturePaintFillSettings { useFirstChannelTransform = linked, color = Color.red });
            layer.channels[TexturePaintChannel.Roughness] = new EditableTextureTarget("Roughness", 64, 64,
                RenderTextureFormat.ARGBHalf, null, Color.clear);
            var master = layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings;
            master.projection = TexturePaintFillProjection.Triplanar;
            master.blendSharpness = 13;
            var roughness = new TexturePaintChannelSourceSettings
            {
                color = Color.gray, blur = 3, multiplier = Color.red, additive = Color.blue,
                tiling = new Vector2(7, 3), rotation = 61, projection = TexturePaintFillProjection.Flat
            };
            layer.GetChannelSettings(TexturePaintChannel.Roughness).sourceSettings = roughness;
            TextureSet.SynchronizeFillChannelTransforms(layer);
            Assert.That(TexturePaintStageWindow.ResolveFillMappingChannel(layer, TexturePaintChannel.Roughness),
                Is.EqualTo(linked ? TexturePaintChannel.Albedo : TexturePaintChannel.Roughness));
            Assert.That(roughness.projection, Is.EqualTo(linked ? master.projection : TexturePaintFillProjection.Flat));
            Assert.That(roughness.tiling, Is.EqualTo(linked ? master.tiling : new Vector2(7, 3)));
            Assert.That(roughness.rotation, Is.EqualTo(linked ? master.rotation : 61));
            Assert.That(roughness.blendSharpness, Is.EqualTo(linked ? 13 : 4));
            Assert.That(roughness.color, Is.EqualTo(Color.gray));
            Assert.That(roughness.blur, Is.EqualTo(3));
            Assert.That(roughness.multiplier, Is.EqualTo(Color.red));
            Assert.That(roughness.additive, Is.EqualTo(Color.blue));
        }
    }
}
#endif
